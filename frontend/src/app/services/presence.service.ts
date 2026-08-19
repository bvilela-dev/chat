import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiRoutes } from '../core/api-routes';
import { UserStatusDto } from '../models/chat.models';

/**
 * Presença: quem está online agora.
 *
 * ---------------------------------------------------------------------------
 * NOTA SOBRE A MUDANÇA DE CONTRATO
 * ---------------------------------------------------------------------------
 * Os métodos de escrita não recebem mais um identificador de usuário. As rotas
 * anteriores eram `/presence/online/{userId}` e `/presence/offline/{userId}`,
 * sem qualquer verificação de que o identificador correspondia ao dono do token.
 *
 * Na prática, qualquer usuário autenticado podia marcar outro como offline
 * (fazendo-o parecer indisponível) ou como online — o que, além do incômodo,
 * suprimiria todas as notificações da vítima, já que o Notification Service só
 * notifica quem está offline.
 * ---------------------------------------------------------------------------
 */
@Injectable({ providedIn: 'root' })
export class PresenceService {
  private readonly http = inject(HttpClient);

  /** Lista os usuários atualmente online. */
  getOnlineUsers(): Observable<UserStatusDto[]> {
    return this.http.get<UserStatusDto[]>(ApiRoutes.presence.onlineUsers);
  }

  /**
   * Marca o próprio usuário como online.
   *
   * Serve também de heartbeat: o registro no Redis tem TTL, e cada chamada o
   * renova. Se o cliente parar de chamar — aba fechada, rede caída, processo
   * morto —, o usuário expira naturalmente para offline, sem depender de um
   * encerramento bem-comportado.
   */
  setSelfOnline(): Observable<UserStatusDto> {
    return this.http.post<UserStatusDto>(ApiRoutes.presence.setSelfOnline, {});
  }

  /** Marca o próprio usuário como offline. */
  setSelfOffline(): Observable<UserStatusDto> {
    return this.http.post<UserStatusDto>(ApiRoutes.presence.setSelfOffline, {});
  }
}
