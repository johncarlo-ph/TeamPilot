import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  InteractionRequiredAuthError,
  PublicClientApplication,
} from '@azure/msal-browser';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/services/auth.service';

interface GoogleCredentialResponse {
  credential: string;
}

declare const window: Window & {
  google?: {
    accounts: {
      id: {
        initialize(config: {
          client_id: string;
          callback: (response: GoogleCredentialResponse) => void;
        }): void;
        renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
      };
    };
  };
};

@Component({
  selector: 'app-login',
  templateUrl: './login.html',
})
export class Login implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly googleConfigured = !!environment.auth.googleClientId;
  readonly microsoftConfigured = !!environment.auth.microsoft.clientId;
  readonly loading = signal(false);
  readonly errorMessage = signal<string | null>(null);

  private msalApp?: PublicClientApplication;

  ngOnInit(): void {
    if (this.googleConfigured) {
      this.initializeGoogleSignIn();
    }
    if (this.microsoftConfigured) {
      this.msalApp = new PublicClientApplication({
        auth: {
          clientId: environment.auth.microsoft.clientId,
          authority: environment.auth.microsoft.authority,
          // Explicit and stable (no query string) so it exactly matches whatever
          // SPA redirect URI is registered in Azure AD for this origin — MSAL's
          // undocumented default of `window.location.href` would otherwise vary
          // with the current URL (e.g. ?returnUrl=...) and could fail to match.
          redirectUri: `${window.location.origin}/login`,
        },
      });
    }
  }

  async signInWithMicrosoft(): Promise<void> {
    if (!this.msalApp) {
      return;
    }
    this.loading.set(true);
    this.errorMessage.set(null);
    try {
      await this.msalApp.initialize();
      const result = await this.msalApp.loginPopup({ scopes: ['openid', 'profile', 'email'] });
      await this.completeLogin('microsoft', result.idToken);
    } catch (error) {
      if (!(error instanceof InteractionRequiredAuthError)) {
        this.errorMessage.set('Microsoft sign-in failed. Please try again.');
      }
      this.loading.set(false);
    }
  }

  private initializeGoogleSignIn(): void {
    const scriptId = 'google-identity-services';
    if (!document.getElementById(scriptId)) {
      const script = document.createElement('script');
      script.id = scriptId;
      script.src = 'https://accounts.google.com/gsi/client';
      script.async = true;
      script.defer = true;
      script.onload = () => this.renderGoogleButton();
      document.head.appendChild(script);
    } else {
      this.renderGoogleButton();
    }
  }

  private renderGoogleButton(): void {
    const google = window.google;
    const container = document.getElementById('google-signin-button');
    if (!google || !container) {
      return;
    }
    google.accounts.id.initialize({
      client_id: environment.auth.googleClientId,
      callback: (response) => this.completeLogin('google', response.credential),
    });
    google.accounts.id.renderButton(container, { theme: 'outline', size: 'large', width: 280 });
  }

  private async completeLogin(provider: 'google' | 'microsoft', idToken: string): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.authService.loginWithIdToken(provider, idToken).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/projects';
        this.router.navigateByUrl(returnUrl);
      },
      error: () => {
        this.errorMessage.set('Sign-in failed. Please try again.');
        this.loading.set(false);
      },
    });
  }
}
