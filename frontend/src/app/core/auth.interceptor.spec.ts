import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from '../services/auth.service';
import { AuthResponse } from '../models/chat.models';

/**
 * Testes do interceptor de autenticação.
 *
 * O comportamento mais importante coberto aqui é a renovação automática de
 * sessão em 401 — inclusive o caso de várias requisições paralelas expirarem ao
 * mesmo tempo, que numa implementação ingênua derrubaria a sessão do usuário
 * por causa da rotação de uso único do refresh token.
 */
describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;

  const sessaoRenovada: AuthResponse = {
    accessToken: 'access-token-novo',
    accessTokenExpiresAtUtc: '2026-01-15T12:30:00Z',
    refreshToken: 'refresh-token-novo',
    refreshTokenExpiresAtUtc: '2026-01-22T12:00:00Z',
    user: {
      id: '22222222-2222-2222-2222-222222222222',
      name: 'Bruno',
      email: 'bruno@teste.dev',
      createdAtUtc: '2026-01-01T00:00:00Z'
    }
  };

  function darSessaoInicial(): void {
    localStorage.setItem('chat.auth', JSON.stringify({
      ...sessaoRenovada,
      accessToken: 'access-token-antigo',
      refreshToken: 'refresh-token-antigo'
    }));
  }

  beforeEach(() => {
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting()
      ]
    });
  });

  afterEach(() => {
    localStorage.clear();
  });

  function setup(): void {
    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  }

  it('deve anexar o cabeçalho Authorization quando há sessão', () => {
    darSessaoInicial();
    setup();

    httpClient.get('/messages/api/conversations').subscribe();

    const requisicao = httpMock.expectOne('/messages/api/conversations');
    expect(requisicao.request.headers.get('Authorization')).toBe('Bearer access-token-antigo');
    requisicao.flush([]);
  });

  it('não deve anexar o cabeçalho quando não há sessão', () => {
    setup();

    httpClient.get('/identity/api/users').subscribe();

    const requisicao = httpMock.expectOne('/identity/api/users');
    expect(requisicao.request.headers.has('Authorization')).toBeFalse();
    requisicao.flush([]);
  });

  it('não deve interferir nos endpoints de autenticação', () => {
    // Um 401 legítimo do login ("senha errada") não pode disparar uma tentativa
    // de refresh: o usuário receberia um erro confuso em vez da mensagem
    // correta. E um 401 do próprio /refresh causaria recursão infinita.
    darSessaoInicial();
    setup();

    httpClient.post('/identity/api/auth/login', {}).subscribe({ error: () => undefined });

    const requisicao = httpMock.expectOne('/identity/api/auth/login');
    expect(requisicao.request.headers.has('Authorization')).toBeFalse();

    requisicao.flush({ title: 'E-mail ou senha inválidos.' }, { status: 401, statusText: 'Unauthorized' });

    // Nenhuma chamada de refresh foi disparada.
    httpMock.expectNone('/identity/api/auth/refresh');
    httpMock.verify();
  });

  it('deve renovar a sessão e reexecutar a requisição após um 401', () => {
    darSessaoInicial();
    setup();

    let resposta: unknown;
    httpClient.get('/messages/api/conversations').subscribe((valor) => (resposta = valor));

    // Primeira tentativa: token expirado.
    httpMock
      .expectOne((request) => request.url === '/messages/api/conversations')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    // O interceptor chama o refresh.
    const requisicaoDeRefresh = httpMock.expectOne('/identity/api/auth/refresh');
    expect(requisicaoDeRefresh.request.body).toEqual({ refreshToken: 'refresh-token-antigo' });
    requisicaoDeRefresh.flush(sessaoRenovada);

    // E reexecuta a original com o token novo. Do ponto de vista do componente
    // que a disparou, nada aconteceu: ele recebe a resposta de sucesso.
    const reexecucao = httpMock.expectOne('/messages/api/conversations');
    expect(reexecucao.request.headers.get('Authorization')).toBe('Bearer access-token-novo');
    reexecucao.flush([{ id: 'conversa-1' }]);

    expect(resposta).toEqual([{ id: 'conversa-1' }]);
    httpMock.verify();
  });

  it('deve executar um único refresh para várias requisições que expiram juntas', () => {
    // A "TEMPESTADE DE REFRESH".
    //
    // A tela de chat dispara várias requisições em paralelo. Quando o token
    // expira, todas tomam 401 quase ao mesmo tempo. Como o backend aplica
    // rotação de USO ÚNICO, se cada uma chamasse /refresh por conta própria, a
    // primeira teria sucesso e as demais apresentariam um token já revogado —
    // derrubando a sessão justamente quando ela deveria ser renovada.
    darSessaoInicial();
    setup();

    httpClient.get('/messages/api/conversations').subscribe();
    httpClient.get('/identity/api/users').subscribe();
    httpClient.get('/presence/api/presence/online').subscribe();

    httpMock.expectOne('/messages/api/conversations')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/identity/api/users')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/presence/api/presence/online')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    // UM ÚNICO refresh — é a asserção central deste teste.
    const requisicoesDeRefresh = httpMock.match('/identity/api/auth/refresh');
    expect(requisicoesDeRefresh.length).toBe(1);
    requisicoesDeRefresh[0].flush(sessaoRenovada);

    // As três reexecutam com o token novo.
    const reexecucoes = httpMock.match(
      (request) => request.headers.get('Authorization') === 'Bearer access-token-novo'
    );
    expect(reexecucoes.length).toBe(3);
    reexecucoes.forEach((requisicao) => requisicao.flush([]));

    httpMock.verify();
  });

  it('deve encerrar a sessão quando o refresh também falha', () => {
    // Refresh token expirado ou revogado: a sessão acabou de verdade. Limpar o
    // estado local e voltar ao login é a única saída correta — insistir deixaria
    // o usuário preso numa tela que só produz erros.
    darSessaoInicial();
    setup();

    const authService = TestBed.inject(AuthService);

    httpClient.get('/messages/api/conversations').subscribe({ error: () => undefined });

    httpMock.expectOne('/messages/api/conversations')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    httpMock.expectOne('/identity/api/auth/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(authService.isAuthenticated()).toBeFalse();
    httpMock.verify();
  });
});
