import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastContainer } from './toast-container';
import { NotificationService } from '../../core/notification/notification.service';

describe('ToastContainer', () => {
  let fixture: ComponentFixture<ToastContainer>;
  let notifications: NotificationService;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ToastContainer] });
    fixture = TestBed.createComponent(ToastContainer);
    notifications = TestBed.inject(NotificationService);
    fixture.detectChanges();
  });

  it('render_Always_PositionsTheStackTopCenteredWithAFixedMaxWidth', () => {
    const stack: HTMLElement = fixture.nativeElement.querySelector('.toast-stack');

    expect(stack.classList).toContain('top-0');
    expect(stack.classList).toContain('start-50');
    expect(stack.classList).toContain('translate-middle-x');
    expect(stack.classList).not.toContain('end-0');
    expect(stack.style.maxWidth).toBe('420px');
  });

  it('show_MessagePosted_RendersItInTheStack', () => {
    notifications.success('Saved successfully.');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Saved successfully.');
  });
});
