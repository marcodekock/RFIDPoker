import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Deck {
  id: number;
  name: string;
  mappingCount: number;
  isEnabled: boolean;
}

@Injectable({ providedIn: 'root' })
export class DecksService {
  constructor(private http: HttpClient) {}

  list(): Observable<Deck[]> {
    return this.http.get<Deck[]>('/api/decks');
  }

  create(name: string): Observable<Deck> {
    return this.http.post<Deck>('/api/decks', { name });
  }

  rename(id: number, name: string): Observable<Deck> {
    return this.http.put<Deck>(`/api/decks/${id}`, { name });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/decks/${id}`);
  }

  setEnabled(id: number, isEnabled: boolean): Observable<void> {
    return this.http.put<void>(`/api/decks/${id}/enabled`, { isEnabled });
  }
}
