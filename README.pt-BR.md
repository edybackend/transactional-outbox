# Transactional Outbox · Inbox

[![CI](https://github.com/edybackend/transactional-outbox/actions/workflows/ci.yml/badge.svg)](https://github.com/edybackend/transactional-outbox/actions/workflows/ci.yml)
[Read in English](README.md)

Mensageria confiável entre dois serviços em .NET 10, sem transação distribuída.
**Subscriptions** grava a assinatura *e* a mensagem sobre ela numa única transação do banco. Um relay publica a
mensagem no RabbitMQ depois. **Billing** consome de forma idempotente e emite a fatura.

Pare o broker, continue criando assinaturas e ligue de novo: nada se perde e nada é cobrado duas vezes. O CI roda
exatamente isso a cada push.

## O problema: escrita dupla

```csharp
await db.SaveChangesAsync();          // 1. a assinatura foi gravada
await broker.PublishAsync(created);   // 2. 💥 broker fora, timeout ou processo morto → o Billing nunca fica sabendo
```

São dois sistemas e duas escritas, sem transação em comum. Inverter as linhas só muda o bug de lugar: a mensagem sai
e a gravação falha. O meu projeto [orders-microservices](https://github.com/edybackend/orders-microservices) registra isso
como trade-off conhecido, e este repositório fecha essa lacuna.

## Como rodar

Requisito: Docker.

```bash
docker compose up --build
```

Abra http://localhost:8080/scalar. Três assinaturas de exemplo já estão faturadas (uma cancelada). Depois teste a queda:

```bash
docker compose stop rabbitmq       # o broker caiu
# crie algumas assinaturas no Scalar: 201 Created, elas esperam em GET /api/outbox?status=Pending
docker compose start rabbitmq      # em até ~30 s (teto do backoff) a fila drena e GET /api/invoices mostra todas
```

| O quê | Onde |
|---|---|
| Referência da API (Scalar) | http://localhost:8080/scalar |
| Painel do RabbitMQ | http://localhost:15672 (guest / guest) |
| Health check | http://localhost:8080/health |

Há também o arquivo [`requests.http`](requests.http) para Visual Studio, Rider ou VS Code.

<details>
<summary>Sem Docker (.NET 10 SDK, broker em memória)</summary>

```bash
dotnet run --project src/TransactionalOutbox.Api   # http://localhost:8080/scalar
```
`PUT /api/broker` com `{ "available": false }` simula a queda, e `{ "available": true }` encerra a queda.
</details>

## Como funciona

```mermaid
flowchart LR
    C[Cliente] -->|POST /api/subscriptions| S[Subscriptions]
    S -->|uma transação| DB1[(subscriptions<br/>+ outbox_messages)]
    R[Relay do outbox] -->|reserva lote com lease| DB1
    R -->|publica e espera confirmação| MQ{{RabbitMQ}}
    R -->|marca publicada / agenda nova tentativa| DB1
    MQ -->|entrega| B[Consumidor Billing]
    B -->|uma transação| DB2[(invoices<br/>+ inbox_messages)]
```

1. **Gravação.** `SubscriptionService` adiciona a assinatura e chama `db.AddToOutbox(...)`. Há um único `SaveChanges`, então a linha e a mensagem são gravadas juntas ou nenhuma das duas. O módulo nunca fala com o broker.
2. **Relay.** Um serviço em segundo plano reserva as mensagens vencidas com um único `UPDATE` que define um lease. Vários relays podem rodar ao mesmo tempo sem publicar a mesma mensagem duas vezes.
3. **Publicação.** Mensagens persistentes com **publisher confirms** e `mandatory`. A mensagem só é marcada como publicada depois que o RabbitMQ confirmou que ela está numa fila.
4. **Nova tentativa.** Uma falha registra o erro e agenda a próxima tentativa com backoff exponencial (1 s, 2 s, 4 s… até 30 s). Depois de 25 tentativas, a mensagem vai para dead-letter e pode ser reenfileirada pela API.
5. **Consumo.** O Billing confere a tabela **inbox**. A fatura e a linha da inbox são gravadas na mesma transação, então uma mensagem reentregue é confirmada e ignorada.

### Como o outbox fica durante uma queda

```
GET /api/outbox?status=Pending
subscription.created.v1  attempts 4  next 15:43:04  "The RabbitMQ connection is down and recovering."
```

## Garantias e como são testadas

| Garantia | Como | Teste |
|---|---|---|
| Nenhuma mensagem perdida | linha no outbox na mesma transação da mudança | broker fora → `201`, mensagem pendente → broker volta → fatura |
| Dois relays saudáveis não publicam a mesma mensagem | reserva com lease num único `UPDATE` | 3 relays ao mesmo tempo sobre 60 mensagens → 60 publicações, todas distintas |
| Relay que caiu não trava para sempre | o lease expira e outro relay assume | `A_message_leased_by_another_relay_is_skipped_until_the_lease_expires` |
| Ordem por assinatura | só a mensagem mais antiga não publicada de cada stream pode sair | `created` falhando segura o `cancelled` dela, nunca o de outras assinaturas |
| Duplicata não faz mal | tabela inbox, chave primária no id da mensagem | mesma mensagem 2× e 5× em paralelo → 1 fatura |
| Comportamento com broker real | job do CI com RabbitMQ; outro job para e religa o RabbitMQ com Compose | `Through_a_real_rabbitmq_a_subscription_is_billed_once`, job `outage` do CI |

Duas mutações provam que os testes não são decorativos: tirar a regra por stream faz o teste de ordem falhar, e tirar a condição do lease faz falhar os testes de lease e de relays simultâneos.

## API

| Método | Rota | O que faz |
|---|---|---|
| `POST` | `/api/subscriptions` | inicia (`Basic`, `Pro`, `Team`) → `subscription.created.v1` no outbox |
| `POST` | `/api/subscriptions/{id}/cancel` | cancela → `subscription.cancelled.v1` (`409` se já cancelada) |
| `GET` | `/api/subscriptions`, `/api/subscriptions/{id}` | assinaturas |
| `GET` | `/api/invoices?subscriptionId=` | faturas emitidas pelo Billing |
| `GET` | `/api/outbox?status=&take=`, `/api/outbox/stats` | mensagens com tentativas, próxima tentativa e último erro |
| `POST` | `/api/outbox/{messageId}/requeue` | reenfileira uma mensagem em dead-letter |
| `GET` / `PUT` | `/api/broker` | estado do transporte / simula queda (só em memória) |

Erros seguem o RFC 9457 (problem details): `400` entrada inválida, `404` id desconhecido, `409` conflito.

## Tecnologias

| Área | Escolha |
|---|---|
| Back-end | .NET 10, minimal APIs, OpenAPI + Scalar |
| Persistência | EF Core 10 + SQLite, um banco por módulo, modo WAL |
| Mensageria | RabbitMQ (`RabbitMQ.Client` 7: publisher confirms, ack manual, DLQ, recuperação automática) ou broker em memória |
| Qualidade | xUnit v3, WebApplicationFactory, NetArchTest, analisadores Roslyn (avisos viram erro) |
| Entrega | Imagem multi-stage sem root, Docker Compose, GitHub Actions |

## Estrutura

```
src/
  Subscriptions/            módulo produtor: assinatura, casos de uso, DbContext com a tabela do outbox
  Subscriptions.Contracts/  eventos de integração compartilhados com os consumidores (único código compartilhado)
  Billing/                  módulo consumidor: faturas, handler idempotente, tabela inbox
  Outbox/                   outbox genérico: mensagem, política de retentativa, despachante com lease, relay, admin
  Messaging/                abstrações de transporte + broker em memória
  Messaging.RabbitMq/       publisher, consumidor e topologia do RabbitMQ
  TransactionalOutbox.Api/  endpoints, mapeamento de erros, composição, dados de exemplo
tests/                      testes de unidade, integração (SQLite, API, RabbitMQ) e arquitetura
specs/                      especificações das funcionalidades (spec primeiro)
```

## Decisões de design

- **Outbox no banco do próprio módulo.** A garantia vem da transação local, e a biblioteca do outbox é genérica para qualquer `DbContext`.
- **Polling, não CDC.** Consultar a cada 500 ms é simples e portátil. CDC (Debezium) eliminaria o polling, ao custo de mais infraestrutura.
- **Lease em vez de lock de linha.** O SQLite não tem `SELECT … FOR UPDATE SKIP LOCKED`, então um único `UPDATE` reserva o lote. No PostgreSQL eu usaria `SKIP LOCKED`.
- **Ordem por stream, não global.** Uma mensagem com falha segura só a sua assinatura, e uma mensagem venenosa não para o outbox inteiro.
- **Pelo menos uma vez + inbox, não "exatamente uma vez".** Entrega exatamente uma vez pela rede não existe; processamento efetivamente uma vez, sim.
- **O broker não trava a inicialização.** A conexão abre no primeiro uso, e a recuperação automática do cliente restaura publisher e consumidor depois de uma queda.

## Testes

```bash
dotnet test                                                  # unidade, integração e arquitetura
RABBITMQ_URL=amqp://guest:guest@localhost:5672 dotnet test   # também passando por um broker real
```

## Trade-offs conhecidos

- **Latência do polling.** Até um intervalo (500 ms) entre o commit e a publicação.
- **Mensagens publicadas ficam guardadas.** Em produção, o outbox apagaria ou arquivaria as publicadas após um prazo de retenção.
- **Bloqueio por stream.** Uma mensagem em dead-letter segura as mensagens seguintes da mesma assinatura até ser reenfileirada. É intencional, porque publicar fora de ordem seria pior.
- **Os dois módulos rodam num processo só**, para a demo ser um contêiner. Eles não compartilham código nem banco, então podem virar dois serviços sem mudar o outbox nem o consumidor.

## Trabalhando com um agente de IA

O erro que este repositório existe para evitar é publicar direto no broker depois do `SaveChanges`. É exatamente o atalho que um assistente de código pegaria, então um hook em [`.claude/`](.claude) recusa isso no módulo Subscriptions, e outro protege os contratos das mensagens publicadas. O restante das regras está no [`CLAUDE.md`](CLAUDE.md).

## Licença

[MIT](LICENSE)
