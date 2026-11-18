#!/usr/bin/env sh
DIR="$(cd "$(dirname "$0")" && pwd)"
export PATH="$HOME/.local/bin:$PATH"
FFDEC_MEMORY="${FFDEC_MEMORY:-1024m}"
exec java -Xmx"$FFDEC_MEMORY" \
  --enable-native-access=ALL-UNNAMED \
  --add-opens=java.base/java.lang=ALL-UNNAMED \
  --add-opens=java.base/java.io=ALL-UNNAMED \
  --add-opens=java.base/java.util=ALL-UNNAMED \
  --add-opens=java.base/sun.nio.fs=ALL-UNNAMED \
  --add-opens=java.desktop/sun.awt=ALL-UNNAMED \
  --add-opens=java.desktop/sun.java2d=ALL-UNNAMED \
  -Djava.net.preferIPv4Stack=true \
  -Djna.nosys=true \
  -Djava.util.Arrays.useLegacyMergeSort=true \
  -jar "$DIR/ffdec.jar" "$@"
