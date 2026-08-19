import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { ApiRoutes } from './api-routes';
import { AuthService } from '../services/auth.service';

/**
 * Estado compartilhado do processo de renovação de token.
 *
 * ---------------------------------------------------------------------------
 * O PROBLEMA DA "TEMPESTADE DE REFRESH"
 * ---------------------------------------------------------------------------
 * A tela de chat dispara várias requisições em paralelo (conversas, diretório,
 * presença). Quando o access token expira, **todas** tomam 401 praticamente ao
 * mesmo tempo.
 *
 * Uma implementação ingênua faria cada uma chamar `/refresh` por conta própria.
 * Como o backend aplica **rotação de uso único**, a primeira chamada teria
 * sucesso e revogaria o token; as outras apresentariam um token já revogado e
 * receberiam 401 — derrubando a sessão do usuário justamente no momento em que
 * ela deveria ter sido renovada.
 *
 * A solução é serializar: a primeira requisição que percebe o 401 executa o
 * refresh; as demais ficam aguardando o resultado e reexecutam com o token novo.
 * ---------------------------------------------------------------------------
 */
let isRefreshing = false;

/**
 * Publica o novo access token para as requisições que ficaram em espera.
 *
 * `null` significa "renovação em andamento"; as requisições enfileiradas
 * aguardam o primeiro valor não nulo.
 */
const refreshedToken$ = new BehaviorSubject<string | null>(null);

/**
 * Interceptor que anexa o token de autenticação e renova a sessão em caso de 401.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // Os endpoints de autenticação não devem receber o token nem entrar no fluxo
  // de renovação. Sem essa exclusão, um 401 legítimo do login ("senha errada")
  // dispararia uma tentativa de refresh — que também falharia, produzindo um
  // erro confuso no lugar da mensagem correta. E um 401 do próprio `/refresh`
  // criaria uma recursão infinita.
  if (isAuthenticationEndpoint(request.url)) {
    return next(request);
  }

  const accessToken = authService.getAccessToken();
  const authenticatedRequest = accessToken ? withBearerToken(request, accessToken) : request;

  return next(authenticatedRequest).pipe(
    catchError((error: unknown) => {
      const isUnauthorized = error instanceof HttpErrorResponse && error.status === 401;

      // Sem refresh token não há o que renovar: o caminho é voltar ao login.
      if (!isUnauthorized || !authService.getRefreshToken()) {
        return throwError(() => error);
      }

      return handleUnauthorized(request, next, authService, router);
    })
  );
};

function handleUnauthorized(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService,
  router: Router
): Observable<HttpEvent<unknown>> {
  // Já há uma renovação em curso: esta requisição espera o token novo em vez de
  // disparar um segundo refresh (que falharia, pela rotação de uso único).
  if (isRefreshing) {
    return refreshedToken$.pipe(
      filter((token): token is string => token !== null),
      take(1),
      switchMap((token) => next(withBearerToken(request, token)))
    );
  }

  isRefreshing = true;

  // `null` sinaliza às próximas requisições que a renovação está em andamento.
  refreshedToken$.next(null);

  return authService.refreshSession().pipe(
    switchMap((response) => {
      isRefreshing = false;

      // Libera as requisições que ficaram aguardando.
      refreshedToken$.next(response.accessToken);

      // Reexecuta a requisição original com o token novo. Do ponto de vista do
      // componente que a disparou, nada aconteceu: ele recebe a resposta de
      // sucesso e nunca fica sabendo que houve um 401 no meio do caminho.
      return next(withBearerToken(request, response.accessToken));
    }),
    catchError((refreshError: unknown) => {
      isRefreshing = false;

      // A renovação falhou: o refresh token expirou ou foi revogado. A sessão
      // acabou de verdade — limpar o estado local e voltar ao login é a única
      // saída correta.
      authService.logout();
      void router.navigate(['/signin']);

      return throwError(() => refreshError);
    })
  );
}

function withBearerToken(request: HttpRequest<unknown>, accessToken: string): HttpRequest<unknown> {
  // `clone` porque HttpRequest é imutável no Angular — alterar a original
  // quebraria a reexecução da requisição pelo próprio interceptor.
  return request.clone({
    setHeaders: { Authorization: `Bearer ${accessToken}` }
  });
}

function isAuthenticationEndpoint(url: string): boolean {
  return (
    url.includes(ApiRoutes.identity.login) ||
    url.includes(ApiRoutes.identity.register) ||
    url.includes(ApiRoutes.identity.refresh)
  );
}
