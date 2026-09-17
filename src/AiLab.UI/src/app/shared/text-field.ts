import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PersistSizeDirective } from './persist-size.directive';
import { Icon } from './icon';

/**
 * A labeled multi-line text field used for the free-text request fields (system prompt, cached
 * context, user context, ...). Same resizable <textarea> box in both modes — `readOnly` just adds
 * the native `readonly` attribute — so a request's fields look and behave identically whether
 * you're viewing them (e.g. a plan's task detail) or editing them, instead of a bespoke read view
 * drifting out of sync with the edit screen. Optionally shows read-only attachment chips below.
 */
@Component({
  selector: 'app-text-field',
  standalone: true,
  imports: [CommonModule, FormsModule, PersistSizeDirective, Icon],
  templateUrl: './text-field.html',
  host: { class: 'block' },
})
export class TextField {
  @Input() label = '';
  @Input() hint = '';
  @Input() value: string | null | undefined = '';
  @Input() rows = 3;
  @Input() readOnly = false;
  /** Key for appPersistSize — remembers a manually-resized textarea's size across reloads. Shared with the edit screen's field so a resize made while viewing sticks when later editing, and vice versa. */
  @Input() persistKey = '';
  @Input() monospace = false;
  @Input() placeholder = '';
  /** Read-only attachment chip labels (e.g. "resume.docx (34.6KB)") shown below the field — upload/remove controls stay with the caller since only the edit screen needs them. */
  @Input() attachmentLabels: string[] = [];
  @Output() valueChange = new EventEmitter<string>();
}
