import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { AuthResponse, UserDto } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storageKey = 'chat.auth';
  private readonly authStateSubject = new BehaviorSubject<AuthResponse | null>(this.loadState());

  readonly authState$ = this.authStateSubject.asObservable();
  readonly user$ = new BehaviorSubject<UserDto | null>(this.authStateSubject.value?.user ?? null);

  register(name: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/identity/api/auth/register', { name, email, password }).pipe(
      tap((response) => this.persistState(response))
    );
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/identity/api/auth/login', { email, password }).pipe(
      tap((response) => this.persistState(response))
    );
  }

  isAuthenticated(): boolean {
    return !!this.authStateSubject.value?.accessToken;
  }

  getAccessToken(): string | null {
    return this.authStateSubject.value?.accessToken ?? null;
  }

  logout(): void {
    localStorage.removeItem(this.storageKey);
    this.authStateSubject.next(null);
    this.user$.next(null);
  }

  private persistState(response: AuthResponse): void {
    localStorage.setItem(this.storageKey, JSON.stringify(response));
    this.authStateSubject.next(response);
    this.user$.next(response.user);
  }

  private loadState(): AuthResponse | null {
    const raw = localStorage.getItem(this.storageKey);
    return raw ? JSON.parse(raw) as AuthResponse : null;
  }
}