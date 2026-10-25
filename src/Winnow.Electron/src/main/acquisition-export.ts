import { writeFile } from 'node:fs/promises'
import type { FilePickerOptions } from './file-picker'

export async function saveAcquisitions(
  csv: string,
  chooseFile: (options: FilePickerOptions) => Promise<string | null>,
): Promise<boolean> {
  const path = await chooseFile({
    title: 'Export acquisitions',
    mode: 'save',
    suggestedName: 'winnow-acquisitions.csv',
    filterName: 'CSV',
    extensions: ['csv'],
  })
  if (!path) return false
  // Match the existing export's UTF-8 signature so spreadsheet apps detect its encoding.
  await writeFile(path, Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(csv, 'utf8')]))
  return true
}
