import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { ReviewForm } from './review-form';
import { AuthService } from '../../../core/services/auth.service';
import { UserDto } from '../../../core/models';

function userWithRoles(roles: UserDto['roles']): UserDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Test User',
    email: 'test@example.com',
    roles,
    status: 'Active',
    createdAtUtc: new Date().toISOString(),
  };
}

describe('ReviewForm', () => {
  let fixture: ComponentFixture<ReviewForm>;
  let authService: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReviewForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(ReviewForm);
    authService = TestBed.inject(AuthService);
    fixture.componentRef.setInput('open', true);
  });

  it('render_AnalystRole_DisablesRejectOption', () => {
    authService['currentUserSignal'].set(userWithRoles(['Analyst']));
    fixture.detectChanges();

    const rejectOption: HTMLOptionElement = fixture.nativeElement.querySelector('option[value="Reject"]');
    expect(rejectOption.disabled).toBe(true);
  });

  it('render_DeveloperRole_EnablesRejectOption', () => {
    authService['currentUserSignal'].set(userWithRoles(['Developer']));
    fixture.detectChanges();

    const rejectOption: HTMLOptionElement = fixture.nativeElement.querySelector('option[value="Reject"]');
    expect(rejectOption.disabled).toBe(false);
  });

  it('submit_RejectDecisionConfirmed_EmitsRejectRequest', () => {
    authService['currentUserSignal'].set(userWithRoles(['Admin']));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const submitted = vi.fn();
    fixture.componentInstance.submitted.subscribe(submitted);
    fixture.componentInstance.form.patchValue({ decision: 'Reject', comments: 'Wrong approach' });

    fixture.componentInstance.submit();

    expect(submitted).toHaveBeenCalledWith({ decision: 'Reject', comments: 'Wrong approach' });
  });

  it('submit_RejectDecisionNotConfirmed_DoesNotEmit', () => {
    authService['currentUserSignal'].set(userWithRoles(['Admin']));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const submitted = vi.fn();
    fixture.componentInstance.submitted.subscribe(submitted);
    fixture.componentInstance.form.patchValue({ decision: 'Reject', comments: '' });

    fixture.componentInstance.submit();

    expect(submitted).not.toHaveBeenCalled();
  });

  it('submit_RejectDecisionAsAnalyst_DoesNotEmitEvenIfConfirmed', () => {
    authService['currentUserSignal'].set(userWithRoles(['Analyst']));
    fixture.detectChanges();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const submitted = vi.fn();
    fixture.componentInstance.submitted.subscribe(submitted);
    fixture.componentInstance.form.patchValue({ decision: 'Reject', comments: '' });

    fixture.componentInstance.submit();

    expect(submitted).not.toHaveBeenCalled();
  });
});
