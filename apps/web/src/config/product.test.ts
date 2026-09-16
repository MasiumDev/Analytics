import { describe, expect, it } from "vitest";
import { resolveProductConfiguration } from "./product";

describe("resolveProductConfiguration", () => {
  it("uses brand-neutral Persian-first defaults", () => {
    expect(resolveProductConfiguration()).toEqual({
      branding: {
        productName: "Product",
        shortName: "Product",
        logoUrl: undefined,
      },
      localization: {
        defaultLocale: "fa-IR",
        direction: "rtl",
        displayTimeZone: "Asia/Tehran",
      },
    });
  });

  it("accepts a deployment-provided brand without changing application code", () => {
    expect(
      resolveProductConfiguration({
        productName: "Example Brand",
        shortName: "Example",
        logoUrl: "https://static.example.test/logo.svg",
        defaultLocale: "en",
        direction: "ltr",
        displayTimeZone: "UTC",
      }),
    ).toEqual({
      branding: {
        productName: "Example Brand",
        shortName: "Example",
        logoUrl: "https://static.example.test/logo.svg",
      },
      localization: {
        defaultLocale: "en",
        direction: "ltr",
        displayTimeZone: "UTC",
      },
    });
  });

  it("falls back safely for blank values and unsupported directions", () => {
    const configuration = resolveProductConfiguration({
      productName: " ",
      direction: "sideways",
    });

    expect(configuration.branding.productName).toBe("Product");
    expect(configuration.localization.direction).toBe("rtl");
  });
});
