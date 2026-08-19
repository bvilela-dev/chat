/**
 * Configuração de DESENVOLVIMENTO.
 *
 * O build de produção substitui este arquivo por `environment.production.ts`
 * (ver `fileReplacements` no angular.json). O padrão é o mecanismo canônico do
 * Angular para configuração por ambiente e tem uma vantagem sobre ler variáveis
 * em runtime: o código do ambiente não escolhido é eliminado pelo tree-shaking,
 * então nada de configuração de produção vaza para o bundle de desenvolvimento.
 */
export const environment = {
  production: false,

  /**
   * Vazio de propósito.
   *
   * Todas as chamadas usam caminhos relativos (`/identity/...`, `/messages/...`).
   * Quem roteia é o nginx do contêiner do frontend, que faz proxy para o API
   * Gateway.
   *
   * A vantagem sobre embutir uma URL absoluta: o navegador nunca faz requisição
   * cross-origin, o que elimina CORS e preflight do caminho crítico — e a mesma
   * imagem Docker funciona em qualquer domínio, sem rebuild.
   */
  apiBaseUrl: '',

  /** Intervalo do heartbeat de presença, em milissegundos. */
  presenceHeartbeatMs: 10_000
} as const;
