import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

/**
 * Testes da guarda de rota.
 *
 * Reforçando o que já está documentado na guarda: ela é experiência de uso, não
 * segurança. Estes testes verificam que o usuário sem sessão é redirecionado
 * antes de o componente carregar — não que a aplicação esteja "protegida".
 * A proteção real está no backend e é coberta pelos testes de autorização em C#.
 */
describe('authGuard', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['isAuthenticated']);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceSpy }
      ]
    });
  });

  function runGuard(url: string): boolean | UrlTree {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url } as never)
    ) as boolean | UrlTree;
  }

  it('deve liberar o acesso quando há sessão ativa', () => {
    authServiceSpy.isAuthenticated.and.returnValue(true);

    expect(runGuard('/chat')).toBeTrue();
  });

  it('deve redirecionar para o login quando não há sessão', () => {
    authServiceSpy.isAuthenticated.and.returnValue(false);

    const resultado = runGuard('/chat');

    expect(resultado).toBeInstanceOf(UrlTree);
  });

  it('deve preservar a rota pretendida em returnUrl', () => {
    // Depois de entrar, o usuário volta para onde queria ir — e não para uma
    // tela genérica, que o obrigaria a navegar de novo.
    authServiceSpy.isAuthenticated.and.returnValue(false);

    const router = TestBed.inject(Router);
    const resultado = runGuard('/chat') as UrlTree;

    expect(router.serializeUrl(resultado)).toContain('returnUrl=%2Fchat');
  });
});
