import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditLogEntryDto } from '../models';

@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/audit-log`;

  list(skip: number, take: number): Observable<AuditLogEntryDto[]> {
    const params = new HttpParams().set('skip', skip).set('take', take);
    return this.http.get<AuditLogEntryDto[]>(this.baseUrl, { params });
  }
}
