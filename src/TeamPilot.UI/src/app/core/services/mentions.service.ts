import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { FileMentionResultDto, ProjectMentionResultDto, TicketMentionResultDto } from '../models';

// Backs the "@" mention-autocomplete dropdown a user gets while writing a ticket's description
// (see MentionAutocomplete). Ticket/project search span every project the caller has access to;
// file search is scoped to one project at a time (the caller merges results across the projects
// it's allowed to file-reference - see MentionEditor).
@Injectable({ providedIn: 'root' })
export class MentionsService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  searchTickets(query: string): Observable<TicketMentionResultDto[]> {
    return this.http.get<TicketMentionResultDto[]>(`${this.apiBaseUrl}/tickets/search`, { params: { query } });
  }

  searchProjects(query: string): Observable<ProjectMentionResultDto[]> {
    return this.http.get<ProjectMentionResultDto[]>(`${this.apiBaseUrl}/projects/search`, { params: { query } });
  }

  searchFiles(projectId: string, query: string): Observable<FileMentionResultDto[]> {
    return this.http.get<FileMentionResultDto[]>(`${this.apiBaseUrl}/projects/${projectId}/files/search`, { params: { query } });
  }
}
