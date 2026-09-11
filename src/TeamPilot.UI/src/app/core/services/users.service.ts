import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SetUserProjectsRequest, SetUserRolesRequest, SetUserStatusRequest, UserDto } from '../models';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/users`;

  list(): Observable<UserDto[]> {
    return this.http.get<UserDto[]>(this.baseUrl);
  }

  getById(id: string): Observable<UserDto> {
    return this.http.get<UserDto>(`${this.baseUrl}/${id}`);
  }

  setRoles(id: string, request: SetUserRolesRequest): Observable<UserDto> {
    return this.http.put<UserDto>(`${this.baseUrl}/${id}/roles`, request);
  }

  setStatus(id: string, request: SetUserStatusRequest): Observable<UserDto> {
    return this.http.put<UserDto>(`${this.baseUrl}/${id}/status`, request);
  }

  setProjects(id: string, request: SetUserProjectsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/projects`, request);
  }

  getProjects(id: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/${id}/projects`);
  }
}
