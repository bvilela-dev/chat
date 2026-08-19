import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { forkJoin, map, Observable } from 'rxjs';
import { ApiRoutes } from '../core/api-routes';
import { OnlineUser, UserDto, UserStatusDto } from '../models/chat.models';

/** Diretório de contatos, já combinado com o estado de presença. */
export interface UserDirectoryState {
  users: UserDto[];
  contacts: OnlineUser[];
}

/**
 * Monta a lista de contatos cruzando dois serviços distintos.
 *
 * O diretório vem do Identity Service e a presença do Presence Service. É a
 * "composição na borda": em vez de criar um endpoint agregador no backend — que
 * acoplaria os dois serviços —, o cliente busca de ambos em paralelo e combina.
 *
 * O `forkJoin` dispara as duas requisições ao mesmo tempo e só emite quando as
 * duas respondem. Encadeá-las somaria as latências sem necessidade, já que uma
 * não depende da outra.
 */
@Injectable({ providedIn: 'root' })
export class UserDirectoryService {
  private readonly http = inject(HttpClient);

  getDirectory(currentUserId: string): Observable<UserDirectoryState> {
    return forkJoin({
      users: this.http.get<UserDto[]>(ApiRoutes.identity.users),
      statuses: this.http.get<UserStatusDto[]>(ApiRoutes.presence.onlineUsers)
    }).pipe(
      map(({ users, statuses }) => {
        // Map em vez de `.find()` dentro do laço: transforma a combinação de
        // O(n × m) em O(n + m). Irrelevante com dez contatos, relevante com mil.
        const statusByUserId = new Map(statuses.map((status) => [status.userId, status]));

        const directoryUsers = users
          .filter((user) => user.id !== currentUserId)
          .sort((left, right) => left.name.localeCompare(right.name));

        const contacts: OnlineUser[] = directoryUsers
          .map((user) => ({
            ...user,
            isOnline: statusByUserId.has(user.id),
            lastSeenAtUtc: statusByUserId.get(user.id)?.lastSeenAtUtc ?? null
          }))
          // Online primeiro, depois em ordem alfabética: quem está disponível
          // agora é o que interessa ao usuário.
          .sort((left, right) => {
            if (left.isOnline !== right.isOnline) {
              return left.isOnline ? -1 : 1;
            }

            return left.name.localeCompare(right.name);
          });

        return { users: directoryUsers, contacts };
      })
    );
  }
}
