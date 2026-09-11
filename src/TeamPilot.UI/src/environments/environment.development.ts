export const environment = {
  production: false,
  apiBaseUrl: 'https://localhost:7085/api',
  auth: {
    // Fill in with your OAuth client IDs (must match Auth:Providers:*:Audience in the
    // API's appsettings/user-secrets) to exercise real Google/Microsoft sign-in locally.
    googleClientId: '472031968512-m2ja3v68ob0jkhfq9v4pb0ntji742eid.apps.googleusercontent.com',
    microsoft: {
      clientId: '',
      authority: 'https://login.microsoftonline.com/common',
    },
  },
};
