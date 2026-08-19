/**
 * Configuração de PRODUÇÃO.
 *
 * Substitui `environment.ts` durante o build de produção.
 */
export const environment = {
  production: true,

  /** Caminhos relativos: o nginx do contêiner faz o proxy para o gateway. */
  apiBaseUrl: '',

  /** Heartbeat mais espaçado em produção, para reduzir tráfego. */
  presenceHeartbeatMs: 15_000
} as const;
