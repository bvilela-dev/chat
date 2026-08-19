import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Guarda de rota que exige sessão ativa.
 *
 * ---------------------------------------------------------------------------
 * O QUE ISTO CORRIGE
 * ---------------------------------------------------------------------------
 * A rota `/chat` não tinha nenhuma proteção. Acessá-la sem sessão carregava o
 * componente inteiro, que então disparava chamadas à API, tomava uma série de
 * 401 e só depois exibia um estado vazio confuso.
 *
 * Com a guarda, o redirecionamento acontece antes de o componente sequer ser
 * instanciado.
 *
 * ---------------------------------------------------------------------------
 * O QUE ISTO **NÃO** É
 * ---------------------------------------------------------------------------
 * Isto NÃO é um controle de segurança. É experiência de uso.
 *
 * A distinção é importante e costuma ser cobrada em entrevista: qualquer pessoa
 * pode abrir o DevTools e chamar `router.navigate(['/chat'])` diretamente, ou
 * simplesmente escrever um `curl` contra a API. Uma guarda de rota roda no
 * navegador do usuário — território que ele controla por completo.
 *
 * A segurança real está inteiramente no backend: cada endpoint valida o JWT e
 * verifica a autorização por conta própria. A guarda apenas evita mostrar uma
 * tela quebrada a quem não está autenticado.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  // `returnUrl` preserva o destino pretendido: depois de entrar, o usuário volta
  // exatamente para onde queria ir, em vez de cair numa tela genérica.
  return router.createUrlTree(['/signin'], {
    queryParams: { returnUrl: state.url }
  });
};
