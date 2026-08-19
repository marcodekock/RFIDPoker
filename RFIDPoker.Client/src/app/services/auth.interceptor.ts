import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const token = auth.token();

  // Never attach the user JWT to the read-only overlay data endpoints or the overlay
  // page's hub connection; those authenticate via the ?token= overlay JWT explicitly.
  // NOTE: /api/overlay-token/** is the admin management endpoint and DOES require the
  // user JWT — it must NOT be treated as an overlay endpoint.
  const isOverlayApi = (req.url.startsWith('/api/overlay/') || req.url === '/api/overlay')
                    || req.url.includes('access_token=')
                    || req.url.includes('token=');

  const authReq = (token && !isOverlayApi)
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authReq).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401 && token && !isOverlayApi) {
        auth.logout(false);
        router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => err);
    })
  );
};
