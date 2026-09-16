import type { Metadata } from "next";
import { productConfiguration } from "@/config/product";
import "./globals.css";

export const metadata: Metadata = {
  title: productConfiguration.branding.productName,
  description: "Instagram analytics application workspace",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang={productConfiguration.localization.defaultLocale}
      dir={productConfiguration.localization.direction}
      className="h-full antialiased"
    >
      <body className="flex min-h-full flex-col">{children}</body>
    </html>
  );
}
