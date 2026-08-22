import { Workspace } from "@/components/Workspace";

/**
 * An optional catch-all so every workspace screen has a real, refreshable URL while the application
 * itself stays a single client component. Unrecognised paths fall back to the dashboard.
 */
export default function Page() {
  return <Workspace />;
}
