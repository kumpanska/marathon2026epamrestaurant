import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  exhaustMap,
  finalize,
  forkJoin,
  interval,
  map,
  switchMap,
  tap,
} from 'rxjs';
import { Participant } from '@app/models/group-orders/participant.model';
import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private static readonly SYNC_INTERVAL_MS = 15_000;

  private readonly route = inject(ActivatedRoute);
  private readonly groupOrdersService = inject(GroupOrdersService);
  private readonly toastService = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';
  private pollingSubscription: Subscription | null = null;
  private syncInProgress = false;

  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly guestSessionId = signal('');
  readonly groupStatus = signal<GroupReservationStatus | null>(null);
  readonly isLoading = signal(true);
  readonly errorMessage = signal('');
  readonly isSubmitting = signal(false);
  readonly isConfirmationOpen = signal(false);
  readonly lastUpdatedAt = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly isRefreshing = signal(false);
  readonly participantToRemove = signal<Participant | null>(null);
  readonly isRemoving = signal(false);

  readonly consolidatedOrder = computed(() => consolidatePreOrder(this.participants()));
  readonly hasAnyDishes = computed(() =>
    this.participants().some((participant) => participant.items.length > 0)
  );
  readonly pendingParticipantCount = computed(
    () => this.participants().filter((participant) => !participant.isSubmitted).length
  );
  readonly pendingParticipantsMessage = computed(() => {
    const count = this.pendingParticipantCount();
    if (count === 0) {
      return '';
    }

    const noun = count === 1 ? 'participant' : 'participants';
    const verb = count === 1 ? "hasn't" : "haven't";
    return `${count} ${noun} ${verb} submitted their cart yet — their dishes won't be included in the order`;
  });
  readonly statusLabel = computed(() => {
    switch (this.groupStatus()) {
      case 'Open':
        return 'Collecting pre-orders';
      case 'Submitted':
        return 'Sent to Restaurant';
      case 'Closed':
        return 'Closed';
      default:
        return '';
    }
  });
  readonly canSubmit = computed(
    () =>
      this.groupStatus() === 'Open' &&
      this.consolidatedOrder().items.length > 0 &&
      !this.isSubmitting()
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.stopPolling();
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.groupOrdersService
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.groupOrdersService.getSessions$(invite.GroupReservationId),
            guestSessionId: this.groupOrdersService.getOwnerSessionId$(this.reservationId),
            status: this.groupOrdersService.getStatus$(invite.GroupReservationId),
          }).pipe(map((data) => ({ invite, ...data })))
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: ({ invite, sessions, guestSessionId, status }) => {
          this.invite.set(invite);
          this.participants.set(sessions.participants);
          this.guestSessionId.set(guestSessionId);
          this.groupStatus.set(status.status);
          this.lastUpdatedAt.set(new Date());
          this.refreshFailed.set(false);
          this.isLoading.set(false);
          this.startPolling();
        },
        error: (error: unknown) => {
          this.errorMessage.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'Group reservation not found'
              : "Couldn't load the group reservation."
          );
          this.isLoading.set(false);
        },
      });
  }

  refresh(): void {
    this.synchronize().subscribe();
  }

  openRemoveConfirmation(participant: Participant): void {
    if (participant.type === 'Guest' && this.groupStatus() === 'Open') {
      this.participantToRemove.set(participant);
    }
  }

  closeRemoveConfirmation(): void {
    if (!this.isRemoving()) {
      this.participantToRemove.set(null);
    }
  }

  confirmRemove(): void {
    const participant = this.participantToRemove();
    const groupReservationId = this.invite()?.GroupReservationId;
    if (
      !participant ||
      participant.type !== 'Guest' ||
      !groupReservationId ||
      this.groupStatus() !== 'Open' ||
      this.isRemoving()
    ) {
      return;
    }

    this.isRemoving.set(true);
    this.groupOrdersService
      .removeSession$(groupReservationId, participant.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (sessions) => {
          this.participants.set(sessions.participants);
          this.lastUpdatedAt.set(new Date());
          this.refreshFailed.set(false);
          this.participantToRemove.set(null);
          this.isRemoving.set(false);
          this.toastService.success('Guest removed from the reservation');
        },
        error: () => {
          this.isRemoving.set(false);
        },
      });
  }

  openConfirmation(): void {
    if (this.canSubmit()) {
      this.isConfirmationOpen.set(true);
    }
  }

  closeConfirmation(): void {
    if (!this.isSubmitting()) {
      this.isConfirmationOpen.set(false);
    }
  }

  submit(): void {
    const groupReservationId = this.invite()?.GroupReservationId;
    if (!groupReservationId || !this.canSubmit()) {
      return;
    }

    this.isSubmitting.set(true);
    this.groupOrdersService
      .submit$(groupReservationId, this.reservationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.groupStatus.set('Submitted');
          this.stopPolling();
          this.isSubmitting.set(false);
          this.isConfirmationOpen.set(false);
          this.toastService.success('Pre-order sent to the restaurant');
        },
        error: () => {
          this.isSubmitting.set(false);
        },
      });
  }

  private startPolling(): void {
    this.stopPolling();
    if (this.groupStatus() !== 'Open') {
      return;
    }

    this.pollingSubscription = interval(GroupOrderPageComponent.SYNC_INTERVAL_MS)
      .pipe(
        exhaustMap(() => this.synchronize()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private stopPolling(): void {
    this.pollingSubscription?.unsubscribe();
    this.pollingSubscription = null;
  }

  private synchronize(): Observable<void> {
    const groupReservationId = this.invite()?.GroupReservationId;
    if (!groupReservationId || this.groupStatus() !== 'Open' || this.syncInProgress) {
      return EMPTY;
    }

    this.syncInProgress = true;
    this.isRefreshing.set(true);

    return forkJoin({
      sessions: this.groupOrdersService.getSessions$(groupReservationId, { silent: true }),
      status: this.groupOrdersService.getStatus$(groupReservationId, { silent: true }),
    }).pipe(
      tap(({ sessions, status }) => {
        this.participants.set(sessions.participants);
        this.groupStatus.set(status.status);
        this.lastUpdatedAt.set(new Date());
        this.refreshFailed.set(false);
        if (status.status !== 'Open') {
          this.stopPolling();
        }
      }),
      map(() => undefined),
      catchError(() => {
        this.refreshFailed.set(true);
        return EMPTY;
      }),
      finalize(() => {
        this.syncInProgress = false;
        this.isRefreshing.set(false);
      })
    );
  }
}
