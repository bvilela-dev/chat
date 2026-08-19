import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { authInterceptor } from './core/auth.interceptor';
import { routes } from './app.routes';

/**
 * Configuração raiz da aplicação (Angular standalone, sem NgModule).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(
      // Usa a API `fetch` do navegador em vez de XMLHttpRequest: é a base
      // moderna, com melhor suporte a streaming e a cancelamento via
      // AbortController.
      withFetch(),

      // O interceptor anexa o token e renova a sessão automaticamente em 401.
      withInterceptors([authInterceptor])
    ),

    provideRouter(
      routes,
      // Permite que parâmetros de rota sejam vinculados diretamente a `@Input()`
      // do componente, eliminando a leitura manual do ActivatedRoute.
      withComponentInputBinding()
    ),

    // `eventCoalescing` agrupa múltiplos eventos do navegador num único ciclo de
    // detecção de mudanças. Importa aqui porque o chat recebe mensagens em
    // rajada pelo WebSocket — sem o agrupamento, cada uma dispararia seu próprio
    // ciclo.
    provideZoneChangeDetection({ eventCoalescing: true })
  ]
};
