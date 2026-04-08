import { Routes } from '@angular/router';
import { ChatComponent } from './pages/chat.component';
import { LoginComponent } from './pages/login.component';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: 'login', component: LoginComponent },
  { path: 'chat', component: ChatComponent }
];