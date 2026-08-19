import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiRoutes } from '../core/api-routes';
import { AuthResponse, UserDto } from '../models/chat.models';

/**
 * Sessão do usuário: cadastro, login, renovação e persistência do estado.
 *
 * ---------------------------------------------------------------------------
 * ONDE O TOKEN É GUARDADO, E POR QUÊ ISSO MERECE UMA RESSALVA
 * ---------------------------------------------------------------------------
 * O estado da sessão vai para o `localStorage`. É a escolha pragmática para uma
 * SPA com API separada, mas tem um custo real que vale declarar em vez de
 * esconder: **o `localStorage` é legível por qualquer JavaScript da página**.
 * Uma falha de XSS — própria ou de uma dependência comprometida — expõe o token.
 *
 * A alternativa mais segura é o refresh token viajar num cookie `HttpOnly`,
 * inacessível ao JavaScript, com o access token mantido apenas em memória. Isso
 * exige suporte a cookies e proteção contra CSRF no backend, e ficou fora do
 * escopo desta versão.
 *
 * Mitigações já aplicadas do lado do servidor: access token de vida curta
 * (15 min) e rotação de uso único do refresh token, que torna um vazamento
 * detectável — quando o atacante usa o token, a sessão da vítima cai.
 *
 * ---------------------------------------------------------------------------
 * POR QUE SIGNALS EM VEZ DE BehaviorSubject
 * ---------------------------------------------------------------------------
 * A versão anterior mantinha dois `BehaviorSubject` (estado e usuário) que
 * precisavam ser atualizados juntos, manualmente, a cada mudança — um convite a
 * esquecer um deles e deixar a interface inconsistente.
 *
 * Com signals, existe **uma única fonte de verdade** (`authState`) e tudo o mais
 * é derivado dela via `computed`. Não há como um valor derivado ficar
 * dessincronizado: ele é recalculado por construção.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly storageKey = 'chat.auth';

  /** Fonte única de verdade da sessão. */
  private readonly authState = signal<AuthResponse | null>(this.loadPersistedState());

  /** Usuário autenticado, derivado do estado da sessão. */
  readonly currentUser = computed<UserDto | null>(() => this.authState()?.user ?? null);

  /** Indica se há sessão ativa. */
  readonly isSignedIn = computed<boolean>(() => this.authState()?.accessToken !== undefined);

  /** Cria uma conta e já estabelece a sessão. */
  register(name: string, email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(ApiRoutes.identity.register, { name, email, password })
      .pipe(tap((response) => this.persistState(response)));
  }

  /** Autentica com e-mail e senha. */
  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(ApiRoutes.identity.login, { email, password })
      .pipe(tap((response) => this.persistState(response)));
  }

  /**
   * Troca o refresh token atual por um novo par de tokens.
   *
   * Chamado pelo interceptor quando uma requisição toma 401. O backend aplica
   * rotação: o token enviado aqui é revogado no processo, e o novo precisa
   * substituí-lo no armazenamento — daí o `tap` obrigatório.
   */
  refreshSession(): Observable<AuthResponse> {
    const refreshToken = this.authState()?.refreshToken;

    if (!refreshToken) {
      throw new Error('Não há refresh token disponível.');
    }

    return this.http
      .post<AuthResponse>(ApiRoutes.identity.refresh, { refreshToken })
      .pipe(tap((response) => this.persistState(response)));
  }

  /** Indica se há sessão ativa (versão imperativa, para uso na guarda de rota). */
  isAuthenticated(): boolean {
    return this.isSignedIn();
  }

  /** Access token atual, ou `null`. */
  getAccessToken(): string | null {
    return this.authState()?.accessToken ?? null;
  }

  /** Refresh token atual, ou `null`. */
  getRefreshToken(): string | null {
    return this.authState()?.refreshToken ?? null;
  }

  /** Encerra a sessão local. */
  logout(): void {
    localStorage.removeItem(this.storageKey);
    this.authState.set(null);
  }

  private persistState(response: AuthResponse): void {
    localStorage.setItem(this.storageKey, JSON.stringify(response));
    this.authState.set(response);
  }

  /**
   * Lê o estado persistido na inicialização.
   *
   * O `try/catch` não é excesso de zelo: o conteúdo do `localStorage` é
   * essencialmente entrada não confiável. Ele pode ter sido escrito por uma
   * versão anterior do aplicativo, com outro formato, ou simplesmente ter sido
   * editado à mão. Sem o tratamento, um JSON corrompido lançaria durante a
   * construção do serviço — e o aplicativo inteiro ficaria com a tela em branco,
   * sem nenhuma forma de o usuário se recuperar.
   *
   * Descartar o estado inválido apenas força um novo login, que é a degradação
   * correta.
   */
  private loadPersistedState(): AuthResponse | null {
    const raw = localStorage.getItem(this.storageKey);

    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as AuthResponse;

      // Checagem mínima de forma: um objeto sem accessToken não é uma sessão
      // utilizável, independentemente de o JSON ser sintaticamente válido.
      return parsed?.accessToken ? parsed : null;
    } catch {
      localStorage.removeItem(this.storageKey);
      return null;
    }
  }
}
