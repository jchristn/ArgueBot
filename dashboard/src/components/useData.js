import { useCallback, useEffect, useRef, useState } from "react";
export function useData(key, loader) {
  const load = useRef(loader);
  load.current = loader;
  const mounted = useRef(false);
  const generation = useRef(0);
  const [result, setResult] = useState({
    data: null,
    error: null,
    loading: true,
  });
  const reload = useCallback(async () => {
    const current = ++generation.current;
    try {
      const data = await load.current();
      if (mounted.current && current === generation.current)
        setResult({ data, error: null, loading: false });
    } catch (error) {
      if (mounted.current && current === generation.current)
        setResult((previous) => ({ ...previous, error, loading: false }));
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    reload();
    return () => {
      mounted.current = false;
    };
  }, [key, reload]);
  return { ...result, reload };
}
