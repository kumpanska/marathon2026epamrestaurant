import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CardReservationComponent } from './card-reservation.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';

const RESERVATION: ReservationCustomer = {
  id: 'res-1',
  tableId: 'table-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 2,
  status: ReservationStatusCustomer.Reserved,
  restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48',
  waiterId: 'waiter-1',
};

function findGroupButton(root: HTMLElement): HTMLButtonElement | undefined {
  return Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find(
    (candidate) => candidate.textContent?.trim() === 'Group Pre-Order',
  );
}

describe('CardReservationComponent', () => {
  let component: CardReservationComponent;
  let fixture: ComponentFixture<CardReservationComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }, provideRouter([])],
      imports: [CardReservationComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CardReservationComponent);
    component = fixture.componentInstance;
    // r — обов'язковий input: без нього компонент падає з NG0950
    fixture.componentRef.setInput('r', RESERVATION);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows a group pre-order action for a customer reservation with multiple guests', () => {
    const groupButton = findGroupButton(fixture.nativeElement as HTMLElement);

    expect(groupButton).toBeTruthy();
  });

  it('does not show a group pre-order action for a single-guest reservation', () => {
    fixture.componentRef.setInput('r', { ...RESERVATION, guestCount: 1 });
    fixture.detectChanges();

    const groupButton = findGroupButton(fixture.nativeElement as HTMLElement);

    expect(groupButton).toBeUndefined();
  });

  it('opens the group order route from the reservation card', async () => {
    const router = TestBed.inject(Router);
    const navigateSpy = spyOn(router, 'navigate').and.resolveTo(true);
    const groupButton = findGroupButton(fixture.nativeElement as HTMLElement);

    groupButton?.click();

    expect(navigateSpy).toHaveBeenCalledWith(['/reservations', RESERVATION.id, 'group-order']);
  });
});