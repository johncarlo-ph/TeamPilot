import { Injectable, inject } from '@angular/core';
import { Observable, Subscriber } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ProjectEventDto } from '../models';
import { AuthService } from './auth.service';

const INITIAL_RECONNECT_DELAY_MS = 1000;
const MAX_RECONNECT_DELAY_MS = 10000;

@Injectable({ providedIn: 'root' })
export class ProjectEventsService {
  private readonly authService = inject(AuthService);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  /**
   * Streams `ProjectEventDto`s for a project from `GET /api/projects/{projectId}/events` (SSE),
   * consumed via `fetch()` + `ReadableStream` rather than native `EventSource` - `EventSource`
   * can only authenticate via a cookie or a query-string token, but this app holds its access
   * token in memory and attaches it as a header (see `auth.interceptor.ts`); reusing that here
   * avoids introducing a second, token-in-URL auth pattern. The trade-off is that `fetch` gives
   * none of `EventSource`'s built-in reconnect behavior, so this reconnects itself with a capped
   * exponential backoff on any stream error or clean close, and never errors the returned
   * Observable itself - callers (the board/ticket-detail poll-replacement pipelines) can merge
   * this in once and leave it running for the page's lifetime.
   */
  stream(projectId: string): Observable<ProjectEventDto> {
    return new Observable<ProjectEventDto>((subscriber) => {
      const abortController = new AbortController();
      let stopped = false;
      let reconnectDelayMs = INITIAL_RECONNECT_DELAY_MS;
      let reconnectTimeoutId: ReturnType<typeof setTimeout> | undefined;

      const connect = async (): Promise<void> => {
        try {
          const response = await fetch(`${this.apiBaseUrl}/projects/${projectId}/events`, {
            headers: this.authService.accessToken
              ? { Authorization: `Bearer ${this.authService.accessToken}` }
              : {},
            signal: abortController.signal,
          });

          if (!response.ok || !response.body) {
            throw new Error(`SSE connection failed with status ${response.status}`);
          }

          // A connection that gets this far and later drops should retry promptly again, not
          // pick up wherever the backoff from a prior failed attempt left off.
          reconnectDelayMs = INITIAL_RECONNECT_DELAY_MS;

          const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
          let buffer = '';

          while (!stopped) {
            const { done, value } = await reader.read();
            if (done) {
              break;
            }
            buffer += value;

            let separatorIndex = buffer.indexOf('\n\n');
            while (separatorIndex !== -1) {
              this.emitFrame(buffer.slice(0, separatorIndex), subscriber);
              buffer = buffer.slice(separatorIndex + 2);
              separatorIndex = buffer.indexOf('\n\n');
            }
          }
        } catch {
          // Network drop, a non-2xx response, or the server restarting - the client can't tell
          // these apart and all of them warrant the same "reconnect shortly" behavior below.
        }

        if (!stopped) {
          reconnectTimeoutId = setTimeout(() => void connect(), reconnectDelayMs);
          reconnectDelayMs = Math.min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
        }
      };

      void connect();

      return () => {
        stopped = true;
        abortController.abort();
        if (reconnectTimeoutId !== undefined) {
          clearTimeout(reconnectTimeoutId);
        }
      };
    });
  }

  private emitFrame(frame: string, subscriber: Subscriber<ProjectEventDto>): void {
    const dataLines = frame
      .split('\n')
      .filter((line) => line.startsWith('data:'))
      .map((line) => line.slice(5).trim());

    if (dataLines.length === 0) {
      return; // A comment/heartbeat frame (e.g. ": heartbeat") - nothing to emit.
    }

    try {
      subscriber.next(JSON.parse(dataLines.join('\n')) as ProjectEventDto);
    } catch {
      // Malformed frame - ignore rather than tearing down the whole connection over one bad event.
    }
  }
}
