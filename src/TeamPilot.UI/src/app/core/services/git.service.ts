import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateBranchRequest, GitDiffResult, GitMergeConflictResult, TicketDto } from '../models';

@Injectable({ providedIn: 'root' })
export class GitService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  createBranch(request: CreateBranchRequest): Observable<TicketDto> {
    return this.http.post<TicketDto>(`${this.apiBaseUrl}/git/branches`, request);
  }

  diff(projectId: string, source: string, target: string): Observable<GitDiffResult> {
    const params = new HttpParams().set('source', source).set('target', target);
    return this.http.get<GitDiffResult>(`${this.apiBaseUrl}/projects/${projectId}/git/diff`, {
      params,
    });
  }

  conflicts(projectId: string, source: string, target: string): Observable<GitMergeConflictResult> {
    const params = new HttpParams().set('source', source).set('target', target);
    return this.http.get<GitMergeConflictResult>(
      `${this.apiBaseUrl}/projects/${projectId}/git/conflicts`,
      { params }
    );
  }
}
