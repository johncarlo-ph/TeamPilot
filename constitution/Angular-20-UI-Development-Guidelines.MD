# Angular 20+ UI Development Guidelines

## General

- **Human-readable code:** Prioritize clarity over cleverness.

## State and Data Management Best Practices

- **Reactive Forms:** Prefer `FormGroup` and `FormArray` for complex forms.
- **Signals and RxJS:** Use Angular signals for local state and RxJS for asynchronous streams.
- **Avoid overfetching:** Use resolvers or caching strategies to limit unnecessary data requests.
- **Error handling:** Centralize error handling with interceptors and provide user-friendly messages.
- **Async pipes:** Prefer the `async` pipe in templates over manual subscriptions.

## UI/UX and Layout

- **Consistent styling:** Use Bootstrap with custom themes.
- **Accessibility:** Provide ARIA attributes where needed, use semantic HTML, and support keyboard navigation.
- **Responsive design:** Use Flex Layout or CSS Grid to create adaptive layouts.
- **Error placement:** Show validation messages below controls without disrupting the surrounding layout.

## Angular 20+ Specifics

- **Standalone components:** Use standalone components by default; do not introduce NgModules unless required for compatibility.
- **Signals:** Use signals for reactive local state instead of heavy RxJS usage in local contexts.
- **Typed forms:** Use strong typing for form controls and form groups.
- **ESBuild:** Use the Angular build pipeline to benefit from faster builds and tree-shaking by default.
- **Strict mode:** Keep `strictTemplates` and TypeScript `strict` mode enabled for type safety.
