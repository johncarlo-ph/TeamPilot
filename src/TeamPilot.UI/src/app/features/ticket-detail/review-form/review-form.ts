import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
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
   * Approving merges the ticket's linked branch server-side (ApprovalGateService.ApproveAsync),
   * which returns a proper 409 when no branch is linked yet - this client-side guard is a UX
   * nicety on top of that (skips the round trip), not a substitute for it.
   */
  readonly hasLinkedBranch = input(true);
  readonly submitting = input(false);
  readonly closed = output<void>();
  readonly submitted = output<SubmitReviewRequest>();

  readonly form = this.fb.nonNullable.group({
    decision: this.fb.nonNullable.control<ReviewDecision>('Approve'),
    comments: [''],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        this.form.patchValue({
          decision: this.initialDecision(),
          comments: '',
        });
      }
    });
  }

  get canApprove(): boolean {
    return this.authService.canApprove() && this.hasLinkedBranch();
  }

  /** Reject shares Approve's "final decision" role gate (Admin/Developer only), but isn't
   * blocked by a missing branch - there's nothing to merge either way. */
  get canReject(): boolean {
    return this.authService.canApprove();
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
    if (value.decision === 'Reject') {
      if (!this.canReject) {
        return;
      }
      if (!confirm('Reject this ticket? Its branch and commits will be permanently deleted - this cannot be undone.')) {
        return;
      }
    }
    this.submitted.emit({
      decision: value.decision,
      comments: value.comments || null,
    });
  }
}
