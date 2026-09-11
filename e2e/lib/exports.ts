import AdmZip from 'adm-zip';

// Header rows written by CsvExportWriter.
export const CSV_HEADERS = {
  'items.csv':
    'item_id,library_id,library_name,item_type,name,sort_name,original_title,series_name,season_name,season_number,episode_number,production_year,premiere_date,runtime_ticks,date_created,path,parent_id,series_id,season_id,official_rating,community_rating,critic_rating,overview',
  'media_sources.csv':
    'item_id,media_source_id,path,container,size_bytes,bitrate,video_type,width,height,video_range,video_range_type,is_remote,run_time_ticks',
  'media_streams.csv':
    'item_id,media_source_id,stream_index,stream_type,codec,codec_tag,language,title,display_title,is_default,is_forced,is_external,channels,channel_layout,sample_rate,bitrate,width,height,profile,level,pixel_format,aspect_ratio',
  'provider_ids.csv': 'item_id,provider,provider_id',
  'user_data.csv': 'item_id,user_id,user_name,played,is_favorite,play_count,last_played_date,playback_position_ticks',
} as const;

export type CsvFile = keyof typeof CSV_HEADERS;

/** Parses RFC 4180 CSV as CsvExportWriter writes it: quoted fields can hold commas, quotes, and newlines. */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;

  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (quoted) {
      if (character !== '"') {
        field += character;
      } else if (text[index + 1] === '"') {
        field += '"';
        index++;
      } else {
        quoted = false;
      }
    } else if (character === '"') {
      quoted = true;
    } else if (character === ',') {
      row.push(field);
      field = '';
    } else if (character === '\n' || character === '\r') {
      if (character === '\r' && text[index + 1] === '\n') {
        index++;
      }

      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else {
      field += character;
    }
  }

  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }

  return rows;
}

/** An export ZIP as downloaded from the plugin. */
export class ExportArchive {
  private readonly zip: AdmZip;
  readonly files: string[];

  constructor(buffer: Buffer) {
    this.zip = new AdmZip(buffer);
    this.files = this.zip
      .getEntries()
      .filter(entry => !entry.isDirectory)
      .map(entry => entry.entryName)
      .sort();
  }

  text(name: string): string {
    const entry = this.zip.getEntry(name);
    if (!entry) {
      throw new Error(`${name} is not in the archive. Files: ${this.files.join(', ')}`);
    }

    return entry.getData().toString('utf8');
  }

  header(name: CsvFile): string {
    return this.text(name).split(/\r?\n/, 1)[0];
  }

  rows(name: CsvFile): Record<string, string>[] {
    const [header, ...rows] = parseCsv(this.text(name));
    return rows.map(values => Object.fromEntries(header.map((column, index) => [column, values[index] ?? ''])));
  }

  manifest(): any {
    return JSON.parse(this.text('manifest.json'));
  }
}
