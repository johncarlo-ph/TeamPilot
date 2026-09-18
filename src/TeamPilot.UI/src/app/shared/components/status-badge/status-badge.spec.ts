import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusBadge } from './status-badge';

@Component({
  selector: 'app-host',
  imports: [StatusBadge],
  template: `<app-status-badge [value]="value"></app-status-badge>`,
})
class HostComponent {
  value = 'RequestChanges';
}

describe('StatusBadge', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('render_PascalCaseStatus_InsertsSpaceBetweenWords', () => {
    const badge: HTMLElement = fixture.nativeElement.querySelector('span');
    expect(badge.textContent?.trim()).toBe('Request Changes');
  });

  it('render_KnownStatus_AppliesMappedBadgeClass', () => {
    const badge: HTMLElement = fixture.nativeElement.querySelector('span');
    expect(badge.classList).toContain('text-bg-warning');
  });

  it('render_UnknownStatus_FallsBackToSecondaryClass', () => {
    const unknownFixture = TestBed.createComponent(HostComponent);
    unknownFixture.componentInstance.value = 'SomethingUnexpected';
    unknownFixture.detectChanges();
    const badge: HTMLElement = unknownFixture.nativeElement.querySelector('span');
    expect(badge.classList).toContain('text-bg-secondary');
  });

  it('render_TicketStatusValue_PrefixesLabelWithItsBoardIcon', () => {
    const ticketStatusFixture = TestBed.createComponent(HostComponent);
    ticketStatusFixture.componentInstance.value = 'ForReview';
    ticketStatusFixture.detectChanges();
    const badge: HTMLElement = ticketStatusFixture.nativeElement.querySelector('span');
    expect(badge.textContent?.trim()).toBe('👀 For Review');
  });

  it('render_NonTicketStatusValue_RendersLabelWithNoIcon', () => {
    const badge: HTMLElement = fixture.nativeElement.querySelector('span');
    expect(badge.textContent?.trim()).toBe('Request Changes');
  });
});
