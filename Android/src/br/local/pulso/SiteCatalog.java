package br.local.pulso;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.EnumMap;

/** The bundled tables contain sorted ASCII domain names. Lookup only compares DNS labels. */
final class SiteCatalog {
    enum Category {
        PORN("pornografia","Pornografia"), GAMBLING("apostas","Apostas");
        final String key, label;
        Category(String key, String label) { this.key = key; this.label = label; }
    }
    enum Mode {
        OFF("desativado","Desativado"), IN_FOCUS("foco","Durante o foco"), ALWAYS("sempre","Sempre");
        final String key, label;
        Mode(String key, String label) { this.key = key; this.label = label; }
        static Mode read(String key) {
            for (Mode mode : values()) if (mode.key.equals(key)) return mode;
            return OFF;
        }
    }
    static final class Settings {
        private final EnumMap<Category,Mode> modes = new EnumMap<>(Category.class);
        Mode get(Category category) { return modes.getOrDefault(category, Mode.OFF); }
        void set(Category category, Mode mode) { modes.put(category,mode); }
        boolean usesFocus() { return modes.containsValue(Mode.IN_FOCUS); }
        boolean active(long focusEnd) { return modes.containsValue(Mode.ALWAYS) || usesFocus() && focusEnd != 0; }
    }

    private final Domains porn, gambling;
    SiteCatalog(InputStream porn, InputStream gambling) throws IOException {
        this.porn = new Domains(porn);
        this.gambling = new Domains(gambling);
    }
    Limits.Block block(String host, Settings modes, long focusEnd) {
        if (host == null) return null;
        Limits.Block first = match(host, Category.PORN, modes.get(Category.PORN), focusEnd, porn);
        Limits.Block second = match(host, Category.GAMBLING, modes.get(Category.GAMBLING), focusEnd, gambling);
        return Limits.Block.later(first,second);
    }
    private static Limits.Block match(String host, Category category, Mode mode, long focusEnd, Domains domains) {
        long end = mode == Mode.ALWAYS ? Long.MAX_VALUE : focusEnd;
        if (mode == Mode.OFF || end == 0 || !domains.matches(host)) return null;
        return new Limits.Block(end,"Categoria "+category.label+" · "+mode.label);
    }

    /** Retain the ASCII bytes and one offset per line, instead of a Java string per domain. */
    private static final class Domains {
        private final byte[] bytes;
        private final int[] starts;
        Domains(InputStream source) throws IOException {
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] chunk = new byte[32768];
            int read;
            while ((read=source.read(chunk)) != -1) buffer.write(chunk,0,read);
            bytes = buffer.toByteArray();
            int lines = bytes.length == 0 ? 0 : 1;
            for (int i=0; i+1<bytes.length; i++) if (bytes[i]=='\n') lines++;
            starts = new int[lines+1];
            int line = 1;
            for (int i=0; i+1<bytes.length; i++) if (bytes[i]=='\n') starts[line++] = i+1;
            starts[lines] = bytes.length;
        }
        boolean matches(String host) {
            for (int from=0; from<host.length();) {
                int dot = host.indexOf('.',from);
                if (dot < 0) return false;
                int low=0, high=starts.length-2;
                while (low<=high) {
                    int mid = (low+high) >>> 1;
                    int order = compare(mid,host,from);
                    if (order == 0) return true;
                    if (order < 0) low = mid+1;
                    else high = mid-1;
                }
                from = dot+1;
            }
            return false;
        }
        private int compare(int line, String host, int from) {
            int start=starts[line], end=starts[line+1];
            while (end>start && (bytes[end-1]=='\n' || bytes[end-1]=='\r')) end--;
            int length=Math.min(end-start,host.length()-from);
            for (int i=0; i<length; i++) {
                int difference = bytes[start+i] - host.charAt(from+i);
                if (difference!=0) return difference;
            }
            return (end-start) - (host.length()-from);
        }
    }
}
