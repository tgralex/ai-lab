# AiLabUI

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 21.2.24.

## Development server

To start a local development server, run:

```bash
npm start
```

This runs `ng serve` with the `--proxy-config proxy.conf.json` needed to route `/api/*` calls to the backend, after regenerating `src/app/core/build-info.ts`. Running plain `ng serve` skips both, and API calls will fail. The AiLab.Api backend must also be running (see the root [README](../../README.md#run-the-backend)).

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
npm run build
```

This compiles the project and, per this project's `angular.json`, writes the build output directly into `../AiLab.Api/wwwroot` (not the default `dist/` directory) so the AiLab.Api backend can serve it. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
