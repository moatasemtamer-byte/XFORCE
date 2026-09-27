import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { appUrl, siteName } from "@/lib/env";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

const title = siteName();

export const metadata: Metadata = {
  metadataBase: appUrl() ? new URL(appUrl()) : undefined,
  title: {
    default: `${title} — Ladder diagram to MSW conversion`,
    template: `%s · ${title}`,
  },
  description:
    "XforceMsw converts PLC ladder diagrams into MSW deliverables. Upload a drawing, get a validated conversion back, and manage licenses from one dashboard.",
  applicationName: title,
  keywords: [
    "ladder diagram",
    "PLC",
    "MSW",
    "IEC 61131-3",
    "controls engineering",
    "diagram conversion",
  ],
  openGraph: {
    title: `${title} — Ladder diagram to MSW conversion`,
    description:
      "Upload a ladder diagram, get a validated MSW deliverable. Licensing, job history and API access in one place.",
    type: "website",
    siteName: title,
  },
  robots: { index: true, follow: true },
};

export const viewport: Viewport = {
  themeColor: "#05080f",
  colorScheme: "dark",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="min-h-full flex flex-col bg-ink-950 text-mist-200">{children}</body>
    </html>
  );
}
