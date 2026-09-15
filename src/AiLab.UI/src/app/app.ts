import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StatusBar } from './shared/status-bar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, StatusBar],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
