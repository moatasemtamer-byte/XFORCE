import { EngineConvertForm } from "@/components/engine/convert-form";
import { getEngineStatus } from "@/lib/engine";

export const dynamic = "force-dynamic";

export default async function EngineConvertPage() {
  const { error } = await getEngineStatus();
  return <EngineConvertForm error={error} />;
}
