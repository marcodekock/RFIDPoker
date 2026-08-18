import { Routes } from '@angular/router';
import { DisplayComponent } from './display/display.component';
import { ConfigComponent } from './config/config.component';
import { OverlayComponent } from './overlay/overlay.component';
import { ManageComponent } from './manage/manage.component';
import { LoginComponent } from './login/login.component';
import { SetupComponent } from './setup/setup.component';
import { UsersComponent } from './admin/users.component';
import { OverlayTokenComponent } from './admin/overlay-token.component';
import { authGuard, adminGuard } from './services/auth.guards';

export const routes: Routes = [
  { path: '', redirectTo: 'display', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'setup', component: SetupComponent },
  // Overlay authenticates via ?token=, not the user session — must remain reachable
  // without being logged in as a user.
  { path: 'overlay', component: OverlayComponent },
  { path: 'display', component: DisplayComponent, canActivate: [authGuard] },
  { path: 'config', component: ConfigComponent, canActivate: [authGuard] },
  { path: 'manage', component: ManageComponent, canActivate: [authGuard] },
  { path: 'admin/users', component: UsersComponent, canActivate: [adminGuard] },
  { path: 'admin/overlay-token', component: OverlayTokenComponent, canActivate: [adminGuard] }
];
