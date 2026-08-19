# Verificação ponta a ponta

Script que exercita a plataforma **em execução**, cobrindo o que os testes unitários não alcançam:
o comportamento real através da rede, do gateway, do WebSocket e do banco.

## Por que existe

Dois defeitos deste projeto **só apareceram aqui**, e ambos eram invisíveis para os testes unitários:

1. **Rotação de refresh token que nunca persistia** — o EF Core classificava o token novo como
   `Modified` em vez de `Added` e emitia `UPDATE` numa linha inexistente. O defeito estava no
   *mapeamento*, não na lógica.

2. **gRPC quebrado por negociação de protocolo** — o Kestrel não serve HTTP/1.1 e HTTP/2 na mesma
   porta sem TLS. Como a política de acesso *falha fechada*, todos os acessos passaram a ser
   negados — mas as tentativas indevidas continuavam bloqueadas, então uma verificação superficial
   concluiria que a segurança estava correta. Só o **fluxo legítimo** revelou o problema.

A segunda lição é a mais importante: num componente que falha fechado, testar apenas o caminho
negativo dá uma falsa sensação de segurança. Testar que o usuário **legítimo consegue** é tão
essencial quanto testar que o intruso não consegue.

## Como rodar

```bash
# 1. Suba a stack
docker compose -f deploy/docker/docker-compose.yml up -d

# 2. Rode a verificação
cd tests/e2e && npm install && npm test
```

## O que é verificado

| Cenário | Esperado |
|---|---|
| Intruso entra em conversa alheia (SignalR) | bloqueado — 403 |
| Intruso envia mensagem em conversa alheia | bloqueado — 403 |
| Intruso lê histórico de conversa alheia (REST) | bloqueado — 403 |
| Participantes entram na própria conversa | permitido |
| Entrega em tempo real ao destinatário | recebida |
| Mensagem acima do limite de 4000 caracteres | rejeitada pela validação |
| Rotação de refresh token | token novo emitido, antigo revogado |
| Reuso de refresh token já usado | rejeitado — 401 |
| Persistência via CQRS (escrita → evento → projeção) | histórico consistente |
