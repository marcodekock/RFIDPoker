import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';

const TOKEN_KEY = 'rfidpoker.auth.token';
const USER_KEY = 'rfidpoker.auth.user';

export interface LoginResponse {
  token: string;
  username: string;
  roles: string[];
}

export interface AuthUser {
  username: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private tokenSignal = signal<string | null>(this.readToken());
  private userSignal = signal<AuthUser | null>(this.readUser());

  token = this.tokenSignal.asReadonly();
  user = this.userSignal.asReadonly();
  isAuthenticated = computed(() => !!this.tokenSignal());
  isAdmin = computed(() => this.userSignal()?.roles.includes('Admin') ?? false);

  constructor(private http: HttpClient, private router: Router) {}

  needsSetup(): Observable<{ needsSetup: boolean }> {
    return this.http.get<{ needsSetup: boolean }>('/api/auth/setup-status');
  }

  setup(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/setup', { username, password })
      .pipe(tap(r => this.persist(r)));
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { username, password })
      .pipe(tap(r => this.persist(r)));
  }

  logout(navigateToLogin = true): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this.tokenSignal.set(null);
    this.userSignal.set(null);
    if (navigateToLogin) this.router.navigateByUrl('/login');
  }

  private persist(r: LoginResponse) {
    localStorage.setItem(TOKEN_KEY, r.token);
    const user: AuthUser = { username: r.username, roles: r.roles };
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this.tokenSignal.set(r.token);
    this.userSignal.set(user);
  }

  private readToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  private readUser(): AuthUser | null {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) return null;
    try { return JSON.parse(raw) as AuthUser; } catch { return null; }
  }
}
