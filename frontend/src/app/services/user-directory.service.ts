import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { forkJoin, map, Observable } from 'rxjs';
import { OnlineUser, UserDto, UserStatusDto } from '../models/chat.models';

export interface UserDirectoryState {
  users: UserDto[];
  contacts: OnlineUser[];
}

@Injectable({ providedIn: 'root' })
export class UserDirectoryService {
  private readonly http = inject(HttpClient);

  getDirectory(currentUserId: string): Observable<UserDirectoryState> {
    return forkJoin({
      users: this.http.get<UserDto[]>('/identity/api/users'),
      statuses: this.http.get<UserStatusDto[]>('/presence/api/presence/online')
    }).pipe(
      map(({ users, statuses }) => {
        const statusMap = new Map(statuses.map((status) => [status.userId, status]));
        const directoryUsers = users
          .filter((user) => user.id !== currentUserId)
          .sort((left, right) => left.name.localeCompare(right.name));

        const contacts = directoryUsers
          .map((user) => ({
            ...user,
            isOnline: statusMap.has(user.id),
            lastSeenAtUtc: statusMap.get(user.id)?.lastSeenAtUtc ?? null
          }))
          .sort((left, right) => {
            if (left.isOnline !== right.isOnline) {
              return left.isOnline ? -1 : 1;
            }

            return left.name.localeCompare(right.name);
          });

        return {
          users: directoryUsers,
          contacts
        };
      })
    );
  }
}