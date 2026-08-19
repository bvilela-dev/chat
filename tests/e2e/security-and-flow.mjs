/**
 * Verificação ponta a ponta contra a stack em execução.
 *
 * Não substitui os testes unitários: eles cobrem a lógica em isolamento e rodam
 * em milissegundos. Este script cobre o que só se manifesta com tudo integrado —
 * negociação de protocolo, mapeamento do ORM, roteamento do gateway, handshake
 * do WebSocket e a consistência eventual entre escrita e projeção.
 *
 * Uso:
 *   docker compose -f deploy/docker/docker-compose.yml up -d
 *   cd tests/e2e && npm install && npm test
 */

import * as signalR from '@microsoft/signalr';

const GATEWAY = process.env.GATEWAY_URL ?? 'http://localhost:8080';
const SENHA = 'senha-super-segura';

// Sufixo único por execução: o script pode rodar repetidamente contra a mesma
// stack sem esbarrar na restrição de e-mail único.
const SUFIXO = Date.now();

let falhas = 0;

const ok = (msg) => console.log(`  \x1b[32m✓\x1b[0m ${msg}`);
const falha = (msg) => { falhas++; console.log(`  \x1b[31m✗ ${msg}\x1b[0m`); };
const secao = (titulo) => console.log(`\n\x1b[1m─── ${titulo} ───\x1b[0m`);

async function api(path, { method = 'GET', body, token } = {}) {
  const response = await fetch(GATEWAY + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });

  return { status: response.status, body: await response.json().catch(() => null) };
}

const registrar = (nome) =>
  api('/identity/api/auth/register', {
    method: 'POST',
    body: { name: nome, email: `${nome.toLowerCase()}${SUFIXO}@teste.dev`, password: SENHA }
  });

async function conectarHub(token) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${GATEWAY}/ws/chat/hubs/chat`, { accessTokenFactory: () => token })
    .configureLogging(signalR.LogLevel.None)
    .build();

  await connection.start();
  return connection;
}

async function main() {
  console.log(`\x1b[1mVerificação ponta a ponta\x1b[0m — ${GATEWAY}\n`);

  // ---------------------------------------------------------------------------
  // Preparação: três usuários. Ana e Beto conversam; Mallory é a atacante.
  // ---------------------------------------------------------------------------
  const ana = (await registrar('Ana')).body;
  const beto = (await registrar('Beto')).body;
  const mallory = (await registrar('Mallory')).body;

  if (!ana?.accessToken || !beto?.accessToken || !mallory?.accessToken) {
    falha('não foi possível cadastrar os usuários — a stack está no ar?');
    process.exit(1);
  }

  const conversa = (await api('/messages/api/conversations/direct', {
    method: 'POST',
    token: ana.accessToken,
    body: { participantId: beto.user.id }
  })).body;

  console.log(`conversa entre Ana e Beto: ${conversa.id}`);

  const conexaoAna = await conectarHub(ana.accessToken);
  const conexaoBeto = await conectarHub(beto.accessToken);
  const conexaoMallory = await conectarHub(mallory.accessToken);

  const recebidasPorBeto = [];
  const recebidasPorMallory = [];
  conexaoBeto.on('messageReceived', (m) => recebidasPorBeto.push(m));
  conexaoMallory.on('messageReceived', (m) => recebidasPorMallory.push(m));

  // ---------------------------------------------------------------------------
  secao('Controle de acesso — tempo real (SignalR)');
  // ---------------------------------------------------------------------------

  // Antes da correção, este `invoke` era aceito e Mallory passava a RECEBER em
  // tempo real todas as mensagens de uma conversa privada.
  try {
    await conexaoMallory.invoke('JoinConversation', conversa.id);
    falha('FALHA DE SEGURANÇA: intruso entrou numa conversa alheia');
  } catch (error) {
    ok(`entrada em conversa alheia bloqueada: ${limpar(error.message)}`);
  }

  // Antes da correção, Mallory conseguia INJETAR mensagens numa conversa da qual
  // não participava.
  try {
    await conexaoMallory.invoke('SendMessage', {
      conversationId: conversa.id,
      content: 'mensagem injetada por um intruso'
    });
    falha('FALHA DE SEGURANÇA: intruso enviou mensagem em conversa alheia');
  } catch (error) {
    ok(`envio em conversa alheia bloqueado: ${limpar(error.message)}`);
  }

  // ---------------------------------------------------------------------------
  secao('Controle de acesso — leitura (REST)');
  // ---------------------------------------------------------------------------

  const leituraIndevida = await api(
    `/messages/api/conversations/${conversa.id}/messages`,
    { token: mallory.accessToken }
  );
  esperar(leituraIndevida.status === 403,
    `leitura de histórico alheio bloqueada (HTTP ${leituraIndevida.status})`,
    `histórico alheio acessível — HTTP ${leituraIndevida.status}`);

  // Rota antiga, que aceitava o identificador de usuário e não o validava.
  const rotaAntiga = await api(
    `/messages/api/users/${ana.user.id}/conversations`,
    { token: mallory.accessToken }
  );
  esperar(rotaAntiga.status === 404,
    `rota vulnerável /users/{id}/conversations removida (HTTP ${rotaAntiga.status})`,
    `rota vulnerável ainda responde — HTTP ${rotaAntiga.status}`);

  // ---------------------------------------------------------------------------
  secao('Fluxo legítimo');
  // ---------------------------------------------------------------------------

  // ESTE É O TESTE QUE REVELOU O BUG DE gRPC/HTTP2.
  //
  // Como a política de acesso falha fechada, um gRPC quebrado negava TODOS os
  // acessos. Os testes de intrusão acima continuavam passando — dando a
  // impressão de que tudo estava correto —, e só o caminho legítimo expôs o
  // problema.
  try {
    await conexaoAna.invoke('JoinConversation', conversa.id);
    await conexaoBeto.invoke('JoinConversation', conversa.id);
    ok('participantes legítimos entraram na conversa');
  } catch (error) {
    falha(`participante legítimo foi bloqueado: ${limpar(error.message)}`);
  }

  await conexaoAna.invoke('SendMessage', { conversationId: conversa.id, content: 'Oi Beto!' });
  await esperarPor(() => recebidasPorBeto.length > 0, 3000);

  esperar(recebidasPorBeto.length === 1 && recebidasPorBeto[0].content === 'Oi Beto!',
    'destinatário recebeu a mensagem em tempo real',
    'destinatário NÃO recebeu a mensagem');

  esperar(recebidasPorMallory.length === 0,
    'intruso não recebeu nada',
    `FALHA DE SEGURANÇA: intruso recebeu ${recebidasPorMallory.length} mensagem(ns)`);

  // ---------------------------------------------------------------------------
  secao('Validação (o pipeline que antes nunca executava)');
  // ---------------------------------------------------------------------------

  try {
    await conexaoAna.invoke('SendMessage', {
      conversationId: conversa.id,
      content: 'a'.repeat(4001)
    });
    falha('mensagem acima do limite foi aceita');
  } catch (error) {
    ok(`mensagem acima do limite rejeitada: ${limpar(error.message)}`);
  }

  const cadastroInvalido = await api('/identity/api/auth/register', {
    method: 'POST',
    body: { name: '', email: 'nao-e-email', password: 'abc' }
  });
  esperar(cadastroInvalido.status === 400 && cadastroInvalido.body?.errors,
    `cadastro inválido rejeitado com detalhamento por campo (${Object.keys(cadastroInvalido.body?.errors ?? {}).length} campos)`,
    `cadastro inválido não foi rejeitado — HTTP ${cadastroInvalido.status}`);

  // ---------------------------------------------------------------------------
  secao('Rotação de refresh token');
  // ---------------------------------------------------------------------------

  const renovacao = await api('/identity/api/auth/refresh', {
    method: 'POST',
    body: { refreshToken: beto.refreshToken }
  });
  esperar(renovacao.status === 200 && renovacao.body.refreshToken !== beto.refreshToken,
    'sessão renovada com um refresh token novo',
    `renovação falhou — HTTP ${renovacao.status}`);

  const reuso = await api('/identity/api/auth/refresh', {
    method: 'POST',
    body: { refreshToken: beto.refreshToken }
  });
  esperar(reuso.status === 401,
    `reuso do token antigo rejeitado (HTTP ${reuso.status}) — rotação de uso único`,
    `FALHA DE SEGURANÇA: refresh token reutilizável — HTTP ${reuso.status}`);

  // ---------------------------------------------------------------------------
  secao('Persistência via CQRS (escrita → evento → projeção)');
  // ---------------------------------------------------------------------------

  // A projeção é assíncrona: a mensagem só aparece no histórico depois de o
  // evento percorrer outbox → RabbitMQ → consumidor. É exatamente a janela de
  // consistência eventual medida pela métrica `message.projection.lag`.
  const historico = await aguardarHistorico(conversa.id, beto.accessToken);

  esperar(historico.length === 1 && historico[0].content === 'Oi Beto!',
    `histórico persistido e projetado: "${historico[0]?.content}" de ${historico[0]?.senderName}`,
    `histórico inconsistente: ${historico.length} mensagem(ns)`);

  await Promise.all([conexaoAna.stop(), conexaoBeto.stop(), conexaoMallory.stop()]);

  console.log(
    falhas === 0
      ? '\n\x1b[32m\x1b[1mTodas as verificações passaram.\x1b[0m\n'
      : `\n\x1b[31m\x1b[1m${falhas} verificação(ões) falharam.\x1b[0m\n`
  );

  process.exit(falhas === 0 ? 0 : 1);
}

/** Remove o prefixo ruidoso que o SignalR acrescenta às mensagens de erro. */
function limpar(mensagem) {
  return mensagem.replace(/^.*HubException:\s*/, '');
}

function esperar(condicao, mensagemSucesso, mensagemFalha) {
  condicao ? ok(mensagemSucesso) : falha(mensagemFalha);
}

async function esperarPor(condicao, timeoutMs) {
  const limite = Date.now() + timeoutMs;
  while (Date.now() < limite && !condicao()) {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}

/**
 * Consulta o histórico repetidamente até a projeção alcançar a escrita.
 *
 * O polling é a forma honesta de testar consistência eventual. Um `sleep` fixo
 * seria simultaneamente lento (espera mais que o necessário no caso comum) e
 * instável (falha quando a máquina está sobrecarregada).
 */
async function aguardarHistorico(conversationId, token, timeoutMs = 10_000) {
  const limite = Date.now() + timeoutMs;

  while (Date.now() < limite) {
    const { body } = await api(`/messages/api/conversations/${conversationId}/messages`, { token });

    if (Array.isArray(body) && body.length > 0) {
      return body;
    }

    await new Promise((resolve) => setTimeout(resolve, 300));
  }

  return [];
}

main().catch((error) => {
  console.error(`\n\x1b[31mErro inesperado:\x1b[0m ${error.message}`);
  process.exit(1);
});
