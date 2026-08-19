import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { AuthResponse } from '../models/chat.models';

/**
 * Testes do serviço de sessão.
 */
describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const respostaDeLogin: AuthResponse = {
    accessToken: 'access-token',
    accessTokenExpiresAtUtc: '2026-01-15T12:15:00Z',
    refreshToken: 'refresh-token',
    refreshTokenExpiresAtUtc: '2026-01-22T12:00:00Z',
    user: {
      id: '22222222-2222-2222-2222-222222222222',
      name: 'Bruno',
      email: 'bruno@teste.dev',
      createdAtUtc: '2026-01-01T00:00:00Z'
    }
  };

  beforeEach(() => {
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('deve iniciar sem sessão', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.currentUser()).toBeNull();
  });

  it('deve estabelecer a sessão após o login', () => {
    service.login('bruno@teste.dev', 'senha').subscribe();

    httpMock.expectOne('/identity/api/auth/login').flush(respostaDeLogin);

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.currentUser()?.name).toBe('Bruno');
    expect(service.getAccessToken()).toBe('access-token');
  });

  it('deve limpar a sessão no logout', () => {
    service.login('bruno@teste.dev', 'senha').subscribe();
    httpMock.expectOne('/identity/api/auth/login').flush(respostaDeLogin);

    service.logout();

    expect(service.isAuthenticated()).toBeFalse();
    expect(localStorage.getItem('chat.auth')).toBeNull();
  });

  it('deve restaurar a sessão persistida ao iniciar', () => {
    localStorage.setItem('chat.auth', JSON.stringify(respostaDeLogin));

    // Uma instância nova simula o recarregamento da página.
    const novaInstancia = TestBed.inject(AuthService);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const servicoRestaurado = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);

    expect(servicoRestaurado.isAuthenticated()).toBeTrue();
    expect(novaInstancia).toBeTruthy();
  });

  it('deve descartar um estado corrompido em vez de quebrar a aplicação', () => {
    // O localStorage é entrada não confiável: pode ter sido escrito por uma
    // versão anterior do aplicativo ou editado à mão. Sem tratamento, o JSON
    // inválido lançaria durante a construção do serviço e deixaria a aplicação
    // com a tela em branco — sem nenhuma forma de o usuário se recuperar.
    localStorage.setItem('chat.auth', '{ isto nao e json valido');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const servicoRestaurado = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);

    expect(servicoRestaurado.isAuthenticated()).toBeFalse();
    expect(localStorage.getItem('chat.auth')).toBeNull();
  });

  it('deve rejeitar um estado sem access token', () => {
    localStorage.setItem('chat.auth', JSON.stringify({ refreshToken: 'apenas-refresh' }));

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    const servicoRestaurado = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);

    expect(servicoRestaurado.isAuthenticated()).toBeFalse();
  });
});
