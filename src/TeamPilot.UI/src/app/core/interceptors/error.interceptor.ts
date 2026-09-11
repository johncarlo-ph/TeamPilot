import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '../notification/notification.service';

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

const AUTH_ENDPOINT_SEGMENT = '/auth/';

/** Surfaces API `ProblemDetails` errors as toasts; auth-endpoint failures are left to callers. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !req.url.includes(AUTH_ENDPOINT_SEGMENT)) {
        notifications.error(describeError(error));
      }
      return throwError(() => error);
    })
  );
};

function describeError(error: HttpErrorResponse): string {
  const problem = error.error as ProblemDetails | null;

  if (problem?.errors) {
    const firstError = Object.values(problem.errors)[0]?.[0];
    if (firstError) {
      return firstError;
    }
  }

  if (problem?.detail) {
    return problem.detail;
  }

  switch (error.status) {
    case 403:
      return "You don't have permission to do that.";
    case 404:
      return 'The requested item could not be found.';
    case 409:
      return "That action isn't allowed given the current state.";
    case 0:
      return 'Could not reach the server. Please check your connection.';
    default:
      return 'Something went wrong. Please try again.';
  }
}
