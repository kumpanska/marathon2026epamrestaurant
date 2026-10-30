import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DishesService } from '@app/services/dishes/dishes.service';
import { FeedbacksService } from '@app/services/feedbacks/feedbacks.service';
import { LocationsService } from '@app/services/locations/locations.service';
import { ReservationService } from '@app/services/reservation/reservation.service';
import { UsersService } from '@app/services/users/users.service';
import { MainPageComponent } from './main-page.component';

describe('MainPageComponent', () => {
  it('resolves images for the popular dishes', async () => {
    const sharedApiImage = 'https://example.com/shared-dish.jpg';
    const dishesService = jasmine.createSpyObj<DishesService>('DishesService', [
      'getPopularDishes',
    ]);
    const locationsService = jasmine.createSpyObj<LocationsService>('LocationsService', [
      'getLocations',
    ]);
    const reservationService = jasmine.createSpyObj<ReservationService>('ReservationService', [
      'getReservationById',
    ]);
    const usersService = jasmine.createSpyObj<UsersService>('UsersService', [
      'getUserProfileImage',
    ]);
    const feedbacksService = jasmine.createSpyObj<FeedbacksService>('FeedbacksService', [
      'getFeedbackByReservation',
    ]);

    dishesService.getPopularDishes.and.returnValue(
      of([
        {
          id: 'spring-salad',
          name: 'Spring Salad',
          imageUrl: sharedApiImage,
          price: 12,
          weight: '250g',
          state: 'Available',
          dishType: 'Salad',
          description: '',
          calories: '200',
          proteins: '5',
          carbohydrates: '20',
          fats: '10',
          vitamins: 'A',
        },
        {
          id: 'grilled-salmon',
          name: 'Grilled Salmon',
          imageUrl: sharedApiImage,
          price: 20,
          weight: '300g',
          state: 'Available',
          dishType: 'MainCourse',
          description: '',
          calories: '400',
          proteins: '30',
          carbohydrates: '10',
          fats: '20',
          vitamins: 'D',
        },
        {
          id: 'truffle-risotto',
          name: 'Truffle Mushroom Risotto',
          imageUrl: sharedApiImage,
          price: 18,
          weight: '280g',
          state: 'Available',
          dishType: 'MainCourse',
          description: '',
          calories: '350',
          proteins: '12',
          carbohydrates: '45',
          fats: '14',
          vitamins: 'B',
        },
      ])
    );
    locationsService.getLocations.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [MainPageComponent],
      providers: [provideRouter([])],
    })
      .overrideComponent(MainPageComponent, {
        set: {
          providers: [
            { provide: DishesService, useValue: dishesService },
            { provide: LocationsService, useValue: locationsService },
            { provide: ReservationService, useValue: reservationService },
            { provide: UsersService, useValue: usersService },
            { provide: FeedbacksService, useValue: feedbacksService },
          ],
        },
      })
      .compileComponents();

    const fixture = TestBed.createComponent(MainPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.dishes.map((dish) => dish.imageUrl)).toEqual([
      'assets/img/dishes/spring-salad.jpg',
      'assets/img/dish-placeholder.svg',
      'assets/img/dish-placeholder.svg',
    ]);
  });
});