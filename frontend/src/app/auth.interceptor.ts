import { HttpInterceptorFn } from '@angular/common/http';

const authStorageKey = 'chat.auth';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const rawAuthState = localStorage.getItem(authStorageKey);
  if (!rawAuthState) {
    return next(request);
  }

  try {
    const authState = JSON.parse(rawAuthState) as { accessToken?: string };
    if (!authState.accessToken) {
      return next(request);
    }

    return next(request.clone({
      setHeaders: {
        Authorization: `Bearer ${authState.accessToken}`
      }
    }));
  }
  catch {
    return next(request);
  }
};