import { Routes } from '@angular/router';
import { DisplayComponent } from './display/display.component';
import { ConfigComponent } from './config/config.component';

export const routes: Routes = [
  { path: '', redirectTo: 'display', pathMatch: 'full' },
  { path: 'display', component: DisplayComponent },
  { path: 'config', component: ConfigComponent }
];
