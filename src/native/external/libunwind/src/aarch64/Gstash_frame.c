/* libunwind - a platform-independent unwind library
   Copyright (C) 2010, 2011 by FERMI NATIONAL ACCELERATOR LABORATORY
   Copyright (C) 2014 CERN and Aalto University
        Contributed by Filip Nyback

This file is part of libunwind.

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.  */

#include "unwind_i.h"

HIDDEN void
tdep_stash_frame (struct dwarf_cursor *d, struct dwarf_reg_state *rs)
{
  struct cursor *c = (struct cursor *) dwarf_to_cursor (d);
  unw_tdep_frame_t *f = &c->frame_info;

  Debug (4, "ip=0x%lx cfa=0x%lx type %d cfa [where=%d val=%ld] cfaoff=%ld"
         " ra=0x%lx fp [where=%d val=%ld @0x%lx] lr [where=%d val=%ld @0x%lx] "
         "sp [where=%d val=%ld @0x%lx]\n",
         d->ip, d->cfa, f->frame_type,
         rs->reg.where[DWARF_CFA_REG_COLUMN],
         rs->reg.val[DWARF_CFA_REG_COLUMN],
         rs->reg.val[DWARF_CFA_OFF_COLUMN],
         DWARF_GET_LOC(d->loc[rs->ret_addr_column]),
         rs->reg.where[FP], rs->reg.val[FP], DWARF_GET_LOC(d->loc[FP]),
         rs->reg.where[LR], rs->reg.val[LR], DWARF_GET_LOC(d->loc[LR]),
         rs->reg.where[SP], rs->reg.val[SP], DWARF_GET_LOC(d->loc[SP]));

  /* A standard frame is defined as:
      - CFA is register-relative offset off FP or SP;
      - Return address is saved in LR;
      - FP is unsaved or saved at CFA+offset, offset != -1;
      - LR is unsaved or saved at CFA+offset, offset != -1;
      - SP is unsaved or saved at CFA+offset, offset != -1.  */
  if (f->frame_type == UNW_AARCH64_FRAME_OTHER
      && (rs->reg.where[DWARF_CFA_REG_COLUMN] == DWARF_WHERE_REG)
      && (rs->reg.val[DWARF_CFA_REG_COLUMN] == FP
          || rs->reg.val[DWARF_CFA_REG_COLUMN] == SP)
      && labs((long)rs->reg.val[DWARF_CFA_OFF_COLUMN]) < (1 << 29)
      && rs->ret_addr_column == LR
      && (rs->reg.where[FP] == DWARF_WHERE_UNDEF
          || rs->reg.where[FP] == DWARF_WHERE_SAME
          || rs->reg.where[FP] == DWARF_WHERE_CFA
          || (rs->reg.where[FP] == DWARF_WHERE_CFAREL
              && labs((long)rs->reg.val[FP]) < (1 << 29)
              && rs->reg.val[FP]+1 != 0))
      && (rs->reg.where[LR] == DWARF_WHERE_UNDEF
          || rs->reg.where[LR] == DWARF_WHERE_SAME
          || rs->reg.where[LR] == DWARF_WHERE_CFA
          || (rs->reg.where[LR] == DWARF_WHERE_CFAREL
              && labs((long)rs->reg.val[LR]) < (1 << 29)
              && rs->reg.val[LR]+1 != 0))
      && (rs->reg.where[SP] == DWARF_WHERE_UNDEF
          || rs->reg.where[SP] == DWARF_WHERE_SAME
          || rs->reg.where[SP] == DWARF_WHERE_CFA
          || (rs->reg.where[SP] == DWARF_WHERE_CFAREL
              && labs((long)rs->reg.val[SP]) < (1 << 29)
              && rs->reg.val[SP]+1 != 0)))
  {
    /* Save information for a standard frame. */
    f->frame_type = UNW_AARCH64_FRAME_STANDARD;
    f->cfa_reg_sp = (rs->reg.val[DWARF_CFA_REG_COLUMN] == SP);
    f->cfa_reg_offset = rs->reg.val[DWARF_CFA_OFF_COLUMN];
    if (rs->reg.where[FP] == DWARF_WHERE_CFAREL)
      f->fp_cfa_offset = rs->reg.val[FP];
    if (rs->reg.where[LR] == DWARF_WHERE_CFAREL)
      f->lr_cfa_offset = rs->reg.val[LR];
    if (rs->reg.where[SP] == DWARF_WHERE_CFAREL)
      f->sp_cfa_offset = rs->reg.val[SP];
    if (rs->reg.where[FP] == DWARF_WHERE_CFA)
      f->fp_cfa_offset = 0;
    if (rs->reg.where[LR] == DWARF_WHERE_CFA)
      f->lr_cfa_offset = 0;
    if (rs->reg.where[SP] == DWARF_WHERE_CFA)
      f->sp_cfa_offset = 0;
    Debug (4, " standard frame\n");
  }
  else
    Debug (4, " unusual frame\n");
}
