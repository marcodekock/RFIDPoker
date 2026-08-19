import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <ng-container *ngIf="showChrome$ | async; else bare">
      <header class="app-header">
        <h1>RFID Poker</h1>
        <nav>
          <a routerLink="/manage" routerLinkActive="active">Manage</a>
          <a routerLink="/display" routerLinkActive="active">Display</a>
          <a routerLink="/config" routerLinkActive="active">Config</a>
          <a *ngIf="auth.isAdmin()" routerLink="/admin/users" routerLinkActive="active">Users</a>
          <a *ngIf="auth.isAdmin()" routerLink="/admin/overlay-token" routerLinkActive="active">Overlay Token</a>
          <a (click)="logout()" style="cursor:pointer">Sign out</a>
        </nav>
      </header>
      <main><router-outlet></router-outlet></main>
    </ng-container>
    <ng-template #bare><router-outlet></router-outlet></ng-template>
  `,
  styles: [`
    .app-header {
      display: flex; align-items: baseline; justify-content: space-between;
      padding: 14px 24px; background: #0b1116; border-bottom: 1px solid #1c2731;
      color: #e6ecef; font-family: system-ui, -apple-system, Segoe UI, sans-serif;
    }
    .app-header h1 { margin: 0; font-size: 20px; letter-spacing: 0.5px; color: #e6ecef; }
    .app-header nav { display: flex; gap: 4px; }
    .app-header nav a {
      color: #7fb0c8; text-decoration: none; font-size: 14px;
      padding: 6px 12px; border-radius: 4px; transition: background 0.15s, color 0.15s;
    }
    .app-header nav a:hover { color: #b9dceb; background: #172128; }
    .app-header nav a.active { color: #ffffff; background: #1f3a4a; }
    main { display: block; }
  `]
})
export class AppComponent {
  auth = inject(AuthService);
  showChrome$: Observable<boolean>;

  constructor(router: Router) {
    this.showChrome$ = router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      startWith(null),
      map(() => {
        const url = router.url.split('?')[0];
        if (url.startsWith('/overlay') || url.startsWith('/login') || url.startsWith('/setup')) return false;
        return this.auth.isAuthenticated();
      })
    );
  }

  logout() { this.auth.logout(); }
}


