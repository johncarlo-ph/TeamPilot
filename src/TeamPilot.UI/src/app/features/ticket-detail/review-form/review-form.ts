import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Modal } from '../../../shared/components/modal/modal';
import { AuthService } from '../../../core/services/auth.service';
import { ReviewDecision, SubmitReviewRequest } from '../../../core/models';

@Component({
  selector: 'app-review-form',
  imports: [ReactiveFormsModule, Modal],
  templateUrl: './review-form.html',
})
export class ReviewForm {
  private readonly fb = inject(FormBuilder);
  protected readonly authService = inject(AuthService);

  readonly open = input(false);
  /** Preselects the decision (e.g. when triggered from a board drag-and-drop action). */
  readonly initialDecision = input<ReviewDecision>('Approve');
  /**
   * Approving merges the ticket's linked branch server-side (ApprovalGateService.ApproveAsync)
   * and currently throws an unhandled 500 (not a proper validation error) when no branch is
   * linked yet — this is blocked client-side until the backend guards it properly.
   */
  readonly hasLinkedBranch = input(true);
  readonly closed = output<void>();
  readonly submitted = output<SubmitReviewRequest>();

  readonly form = this.fb.nonNullable.group({
    reviewerName: [this.authService.currentUser()?.name ?? '', Validators.required],
    decision: this.fb.nonNullable.control<ReviewDecision>('Approve'),
    comments: [''],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        this.form.patchValue({
          reviewerName: this.authService.currentUser()?.name ?? '',
          decision: this.initialDecision(),
          comments: '',
        });
      }
    });
  }

  get canApprove(): boolean {
    return this.authService.canApprove() && this.hasLinkedBranch();
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    if (value.decision === 'Approve' && !this.canApprove) {
      return;
    }
    this.submitted.emit({
      reviewerName: value.reviewerName,
      decision: value.decision,
      comments: value.comments || null,
    });
  }
}
