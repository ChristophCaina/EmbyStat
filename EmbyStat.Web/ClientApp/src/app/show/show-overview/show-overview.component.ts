import { Component, OnInit, OnDestroy } from '@angular/core';
import { Observable } from 'rxjs/Observable';
import { Subscription } from 'rxjs/Subscription';
import { FormControl } from '@angular/forms';
import { MatDialog } from '@angular/material';

import { NoTypeFoundDialog } from '../../shared/dialogs/no-type-found/no-type-found.component';
import { ShowService } from '../service/show.service';
import { Collection } from '../../shared/models/collection';
import { ShowChartsService } from '../service/show-charts.service';

@Component({
  selector: 'app-show-overview',
  templateUrl: './show-overview.component.html',
  styleUrls: ['./show-overview.component.scss']
})
export class ShowOverviewComponent implements OnInit, OnDestroy {
  private isShowTypePresentSub: Subscription;
  collections$: Observable<Collection[]>;
  selectedCollections: string[];

  collectionsFormControl = new FormControl('', { updateOn: 'blur' });
  typeIsPresent: boolean;

  constructor(
    private showService: ShowService,
    private showChartsService: ShowChartsService,
    public dialog: MatDialog) {
    this.collections$ = this.showService.getCollections();

    this.isShowTypePresentSub = this.showService.checkIfTypeIsPresent().subscribe((typePresent: boolean) => {
      this.typeIsPresent = typePresent;
      if (!typePresent) {
        this.dialog.open(NoTypeFoundDialog,
          {
            width: '550px',
            data: 'SHOWS'
          });
      }
    });
  }

  ngOnInit() {
    this.collectionsFormControl.valueChanges.subscribe(data => {
      this.selectedCollections = data;
    });
  }

  onTabChanged(event): void {
    if (event.index === 1) {
      this.showChartsService.changeOpened(true);
    } else {
      this.showChartsService.changeOpened(false);
    }
  }

  ngOnDestroy(): void {
    if (this.isShowTypePresentSub !== undefined) {
      this.isShowTypePresentSub.unsubscribe();
    }
  }
}
