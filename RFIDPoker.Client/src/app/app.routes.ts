import { Routes } from '@angular/router';
import { DisplayComponent } from './display/display.component';
import { ConfigComponent } from './config/config.component';
import { OverlayComponent } from './overlay/overlay.component';
import { ManageComponent } from './manage/manage.component';

export const routes: Routes = [
  { path: '', redirectTo: 'display', pathMatch: 'full' },
  { path: 'display', component: DisplayComponent },
  { path: 'config', component: ConfigComponent },
  { path: 'overlay', component: OverlayComponent },
  { path: 'manage', component: ManageComponent }
];
