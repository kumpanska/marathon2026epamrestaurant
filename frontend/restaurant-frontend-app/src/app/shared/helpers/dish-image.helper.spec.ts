import { getDishImageUrl, DEFAULT_DISH_IMAGE } from './dish-image.helper';

describe('getDishImageUrl', () => {
  it('uses a distinct local image for each of the four menu salads', () => {
    const sharedApiImage = 'https://example.com/fresh-strawberry-mint-salad.jpg';

    expect(getDishImageUrl('Spring Salad', sharedApiImage)).toBe(
      'assets/img/dishes/spring-salad.jpg'
    );
    expect(getDishImageUrl('Avocado Pine Nut Bowl', sharedApiImage)).toBe(
      'assets/img/dishes/avocado-pine-nut-bowl.jpg'
    );
    expect(getDishImageUrl('Fresh Strawberry Mint Salad', sharedApiImage)).toBe(
      'assets/img/dishes/fresh-strawberry-mint-salad.jpg'
    );
    expect(getDishImageUrl('Roasted Sweet Potato & Lentil Salad', sharedApiImage)).toBe(
      'assets/img/dishes/roasted-sweet-potato-and-lentil-salad.jpg'
    );
  });

  it('preserves API images for dishes without a local menu image', () => {
    expect(getDishImageUrl('Grilled Salmon', 'https://example.com/salmon.jpg')).toBe(
      'https://example.com/salmon.jpg'
    );
  });

  it('uses the generic image when no local or API image is available', () => {
    expect(getDishImageUrl('Grilled Salmon', null)).toBe(DEFAULT_DISH_IMAGE);
  });
});