import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { UserStatusDto } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class PresenceService {
  private readonly http = inject(HttpClient);

  getOnlineUsers(): Observable<UserStatusDto[]> {
    return this.http.get<UserStatusDto[]>('/presence/api/presence/online');
  }

  setOnline(userId: string): Observable<UserStatusDto> {
    return this.http.post<UserStatusDto>(`/presence/api/presence/online/${userId}`, {});
  }

  setOffline(userId: string): Observable<UserStatusDto> {
    return this.http.post<UserStatusDto>(`/presence/api/presence/offline/${userId}`, {});
  }
}