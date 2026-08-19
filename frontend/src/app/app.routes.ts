import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

/**
 * Tabela de rotas da aplicação.
 *
 * As páginas são carregadas sob demanda com `loadComponent` (lazy loading).
 * O ganho é concreto: quem abre a tela de login não baixa o componente de chat
 * — que é o maior do projeto e traz junto o cliente SignalR. O bundle inicial
 * fica menor e a primeira tela pinta mais rápido.
 */
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'signin'
  },
  {
    path: 'signin',
    title: 'Entrar · Chat',
    loadComponent: () => import('./pages/login.component').then((m) => m.SignInComponent)
  },
  {
    path: 'signup',
    title: 'Criar conta · Chat',
    loadComponent: () => import('./pages/signup.component').then((m) => m.SignUpComponent)
  },
  {
    path: 'chat',
    title: 'Chat',
    // A guarda impede que o componente seja instanciado sem sessão.
    // Reforçando: é experiência de uso, não segurança — a proteção real está no
    // backend, que valida o token em cada requisição.
    canActivate: [authGuard],
    loadComponent: () => import('./pages/chat.component').then((m) => m.ChatComponent)
  },
  {
    // Alias mantido por compatibilidade com links antigos.
    path: 'login',
    pathMatch: 'full',
    redirectTo: 'signin'
  },
  {
    // Curinga: qualquer rota desconhecida volta ao início, em vez de exibir uma
    // tela em branco sem explicação.
    path: '**',
    redirectTo: 'signin'
  }
];
