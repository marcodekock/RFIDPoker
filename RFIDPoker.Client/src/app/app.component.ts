import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs/operators';
import { Observable } from 'rxjs';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <ng-container *ngIf="isOverlay$ | async; else shell">
      <router-outlet></router-outlet>
    </ng-container>
    <ng-template #shell>
      <nav>
        <a routerLink="/display" routerLinkActive="active">Display</a>
        <a routerLink="/config" routerLinkActive="active">Configuration</a>
      </nav>
      <div class="container">
        <router-outlet></router-outlet>
      </div>
    </ng-template>
  `,
  styles: []
})
export class AppComponent {
  isOverlay$: Observable<boolean>;

  constructor(router: Router) {
    this.isOverlay$ = router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      startWith(null),
      map(() => router.url.startsWith('/overlay'))
    );
  }
}

