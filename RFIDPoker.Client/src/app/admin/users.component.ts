import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../services/auth.service';

interface UserRow {
  id: string;
  username: string;
  roles: string[];
  isActive: boolean;
  createdAt: string;
}

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">
      <h1>User Accounts</h1>

      <section class="card">
        <h2>Create User</h2>
        <div class="row">
          <input placeholder="Username" [(ngModel)]="newUsername" />
          <input type="password" placeholder="Password" [(ngModel)]="newPassword" />
          <select [(ngModel)]="newRole">
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>
          <button class="primary" (click)="create()" [disabled]="!newUsername || !newPassword">Create</button>
        </div>
        <div class="error" *ngIf="createError()">{{ createError() }}</div>
      </section>

      <section class="card">
        <h2>Users</h2>
        <table>
          <thead>
            <tr><th>Username</th><th>Role</th><th>Status</th><th>Created</th><th></th></tr>
          </thead>
          <tbody>
            <tr *ngFor="let u of users()">
              <td>{{ u.username }}</td>
              <td>
                <select [ngModel]="isAdmin(u) ? 'Admin' : 'User'" (ngModelChange)="changeRole(u, $event)">
                  <option value="User">User</option>
                  <option value="Admin">Admin</option>
                </select>
              </td>
              <td>
                <button (click)="toggleActive(u)" [class.danger]="u.isActive" [class.primary]="!u.isActive">
                  {{ u.isActive ? 'Active' : 'Disabled' }}
                </button>
              </td>
              <td>{{ u.createdAt | date:'short' }}</td>
              <td class="actions">
                <button (click)="resetPassword(u)">Reset Password</button>
                <button class="danger" (click)="del(u)">Delete</button>
              </td>
            </tr>
          </tbody>
        </table>
      </section>
    </div>
  `,
  styles: [`
    .page { min-height: 100vh; background: #0e1418; color: #e6ecef; padding: 24px; font-family: system-ui, sans-serif; }
    header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 20px; }
    header nav a { color: #7fb0c8; margin-left: 16px; text-decoration: none; font-size: 14px; }
    .card { background: #172128; border: 1px solid #253340; border-radius: 10px; padding: 18px; margin-bottom: 20px; }
    .card h2 { margin: 0 0 12px; font-size: 16px; }
    .row { display: flex; gap: 8px; flex-wrap: wrap; }
    input, select { background: #0f171c; border: 1px solid #2b3a46; color: #e6ecef; padding: 7px 10px; border-radius: 4px; font-size: 14px; }
    button { background: #223440; border: 1px solid #33505f; color: #dbe7ee; padding: 6px 12px; border-radius: 4px; cursor: pointer; font-size: 13px; }
    button.primary { background: #1f6a3d; border-color: #2d8f52; }
    button.danger { background: #6a1f26; border-color: #a03039; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid #22303a; font-size: 14px; }
    th { color: #8fa4b0; font-size: 12px; text-transform: uppercase; }
    .actions button { margin-right: 6px; }
    .error { color: #ff8080; background: #3a1f1f; padding: 8px 10px; border-radius: 4px; font-size: 13px; margin-top: 10px; }
  `]
})
export class UsersComponent implements OnInit {
  users = signal<UserRow[]>([]);
  newUsername = '';
  newPassword = '';
  newRole: 'User' | 'Admin' = 'User';
  createError = signal('');

  constructor(private http: HttpClient, private auth: AuthService) {}

  ngOnInit() { this.reload(); }

  isAdmin(u: UserRow) { return u.roles.includes('Admin'); }

  reload() {
    this.http.get<UserRow[]>('/api/users').subscribe(r => this.users.set(r));
  }

  create() {
    this.createError.set('');
    this.http.post('/api/users', { username: this.newUsername, password: this.newPassword, role: this.newRole })
      .subscribe({
        next: () => { this.newUsername = ''; this.newPassword = ''; this.newRole = 'User'; this.reload(); },
        error: e => this.createError.set(e.error?.message || 'Create failed')
      });
  }

  toggleActive(u: UserRow) {
    this.http.put(`/api/users/${u.id}`, { isActive: !u.isActive }).subscribe({
      next: () => this.reload(),
      error: e => alert(e.error?.message || 'Failed')
    });
  }

  changeRole(u: UserRow, role: string) {
    this.http.put(`/api/users/${u.id}`, { role }).subscribe({
      next: () => this.reload(),
      error: e => { alert(e.error?.message || 'Failed'); this.reload(); }
    });
  }

  resetPassword(u: UserRow) {
    const pwd = prompt(`New password for ${u.username}:`);
    if (!pwd) return;
    this.http.post(`/api/users/${u.id}/reset-password`, { newPassword: pwd }).subscribe({
      next: () => alert('Password reset.'),
      error: e => alert(e.error?.message || 'Reset failed')
    });
  }

  del(u: UserRow) {
    if (!confirm(`Delete user "${u.username}"?`)) return;
    this.http.delete(`/api/users/${u.id}`).subscribe({
      next: () => this.reload(),
      error: e => alert(e.error?.message || 'Delete failed')
    });
  }

  logout() { this.auth.logout(); }
}
