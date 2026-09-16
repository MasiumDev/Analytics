export type TextDirection = "ltr" | "rtl";

export type ProductConfiguration = Readonly<{
  branding: Readonly<{
    productName: string;
    shortName: string;
    logoUrl?: string;
  }>;
  localization: Readonly<{
    defaultLocale: string;
    direction: TextDirection;
    displayTimeZone: string;
  }>;
}>;

type ProductConfigurationInput = {
  productName?: string;
  shortName?: string;
  logoUrl?: string;
  defaultLocale?: string;
  direction?: string;
  displayTimeZone?: string;
};

const valueOrDefault = (value: string | undefined, fallback: string) =>
  value?.trim() || fallback;

const optionalValue = (value: string | undefined) => value?.trim() || undefined;

export function resolveProductConfiguration(
  input: ProductConfigurationInput = {},
): ProductConfiguration {
  const productName = valueOrDefault(input.productName, "Product");
  const direction: TextDirection = input.direction === "ltr" ? "ltr" : "rtl";

  return {
    branding: {
      productName,
      shortName: valueOrDefault(input.shortName, productName),
      logoUrl: optionalValue(input.logoUrl),
    },
    localization: {
      defaultLocale: valueOrDefault(input.defaultLocale, "fa-IR"),
      direction,
      displayTimeZone: valueOrDefault(input.displayTimeZone, "Asia/Tehran"),
    },
  };
}

export const productConfiguration = resolveProductConfiguration({
  productName: process.env.NEXT_PUBLIC_PRODUCT_NAME,
  shortName: process.env.NEXT_PUBLIC_PRODUCT_SHORT_NAME,
  logoUrl: process.env.NEXT_PUBLIC_PRODUCT_LOGO_URL,
  defaultLocale: process.env.NEXT_PUBLIC_DEFAULT_LOCALE,
  direction: process.env.NEXT_PUBLIC_TEXT_DIRECTION,
  displayTimeZone: process.env.NEXT_PUBLIC_DISPLAY_TIME_ZONE,
});
