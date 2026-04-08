import { Routes } from '@angular/router';
import { ChatComponent } from './pages/chat.component';
import { SignInComponent } from './pages/login.component';
import { SignUpComponent } from './pages/signup.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'signin' },
  { path: 'signin', component: SignInComponent },
  { path: 'signup', component: SignUpComponent },
  { path: 'login', pathMatch: 'full', redirectTo: 'signin' },
  { path: 'chat', component: ChatComponent }
];