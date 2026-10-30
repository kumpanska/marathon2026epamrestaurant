export const DEFAULT_DISH_IMAGE = 'assets/img/dish-placeholder.svg';

const MENU_DISH_IMAGES: Record<string, string> = {
  'spring-salad': 'assets/img/dishes/spring-salad.jpg',
  'avocado-pine-nut-bowl': 'assets/img/dishes/avocado-pine-nut-bowl.jpg',
  'fresh-strawberry-mint-salad': 'assets/img/dishes/fresh-strawberry-mint-salad.jpg',
  'roasted-sweet-potato-and-lentil-salad':
    'assets/img/dishes/roasted-sweet-potato-and-lentil-salad.jpg',
};

function toImageKey(name: string): string {
  return name
    .toLowerCase()
    .replace(/&/g, 'and')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}

export function getDishImageUrl(name: string, imageUrl?: string | null): string {
  return MENU_DISH_IMAGES[toImageKey(name)] ?? imageUrl ?? DEFAULT_DISH_IMAGE;
}

export function resolveDishImages<T extends { name: string; imageUrl?: string | null }>(
  dishes: T[]
): T[] {
  const apiUrlCounts = new Map<string, number>();
  for (const dish of dishes) {
    if (!MENU_DISH_IMAGES[toImageKey(dish.name)] && dish.imageUrl) {
      apiUrlCounts.set(dish.imageUrl, (apiUrlCounts.get(dish.imageUrl) ?? 0) + 1);
    }
  }

  return dishes.map((dish) => {
    const local = MENU_DISH_IMAGES[toImageKey(dish.name)];
    if (local) return { ...dish, imageUrl: local };

    const isShared = !!dish.imageUrl && (apiUrlCounts.get(dish.imageUrl) ?? 0) > 1;
    return { ...dish, imageUrl: isShared ? DEFAULT_DISH_IMAGE : dish.imageUrl || DEFAULT_DISH_IMAGE };
  });
}